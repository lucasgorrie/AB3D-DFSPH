using SharpDX;
using SharpDX.Direct3D11;
using System.IO;
using System.Runtime.InteropServices;

using Buffer = SharpDX.Direct3D11.Buffer;
using Vector3 = System.Numerics.Vector3;


namespace SPH.Compute
{

    public class FluidCompute : IDisposable
    {

        private const int THREADS = 256;
        private const int SCAN_BLOCK = THREADS * 2;

        private readonly Device _device;
        private readonly DeviceContext _context;

        private ComputeShader _applyForces = null!;
        private ComputeShader _integratePos = null!;
        private ComputeShader _densityAdv = null!;
        private ComputeShader _pressureCorrect = null!;
        private ComputeShader _divergenceAdv = null!;
        private ComputeShader _divergenceCorrect = null!;

        private ComputeShader _computeFactor = null!;
        private ComputeShader _density = null!;
        private ComputeShader _count = null!;

        private ComputeShader _scanBlocks = null!;
        private ComputeShader _scanBlockSums = null!;
        private ComputeShader _addOffsets = null!;
        private ComputeShader _scatter = null!;

        private Buffer _solverConstantBuffer = null!;
        private Buffer _domainBounds = null!;
        private ShaderResourceView _domainBoundsSrv = null!;

        private Buffer _particleBuffer = null!;
        private Buffer _gridConstantBuffer = null!;
        private Buffer _stagingBuffer = null!;

        private Buffer _cellCount = null!;
        private Buffer _cellStart = null!;
        private Buffer _blockSums = null!;

        private Buffer _factor = null!;
        private Buffer _cellIndex = null!;
        private Buffer _localOffset = null!;
        private Buffer _sortedIndices = null!;

        private UnorderedAccessView _particleUav = null!;
        private UnorderedAccessView _cellCountUav = null!;
        private UnorderedAccessView _cellStartUav = null!;
        private UnorderedAccessView _blockSumsUav = null!;

        private UnorderedAccessView _factorUav = null!;
        private UnorderedAccessView _cellIndexUav = null!;
        private UnorderedAccessView _localOffsetUav = null!;
        private UnorderedAccessView _sortedIndicesUav = null!;

        private static readonly int _stride = Marshal.SizeOf<Particle>();

        public FluidCompute(Device device, DeviceContext context)
        {
            _device = device;
            _context = context;
        }

        #region Properties

        private int _capacity;
        public int Capacity
        {
            get => _capacity;
            private set
            {
                if (value != _capacity)
                {
                    _capacity = value;
                }
            }
        }

        private int _particleCount;
        public int ParticleCount
        {
            get => _particleCount;
            set
            {
                if (value != _particleCount)
                {
                    _particleCount = value;
                }
            }
        }

        public uint TableSize { get; private set; }

        #endregion

        // Compile shaders & allocate memory to buffers
        public void Initialize(int capacity)
        {
            Capacity = capacity;
            int bufferWidth = Capacity * _stride;

            string shaderPath = Path.Combine(AppContext.BaseDirectory, "SPH", "Compute", "particles.hlsl");
            string src = File.ReadAllText(shaderPath);

            _applyForces = new ComputeShader(_device, D3DCompiler.Compile(src, "CSApplyForces", "cs_5_0"));
            _integratePos = new ComputeShader(_device, D3DCompiler.Compile(src, "CSIntegratePos", "cs_5_0"));
            _densityAdv = new ComputeShader(_device, D3DCompiler.Compile(src, "CSDensityAdv", "cs_5_0"));
            _pressureCorrect = new ComputeShader(_device, D3DCompiler.Compile(src, "CSPressureCorrect", "cs_5_0"));
            _divergenceAdv = new ComputeShader(_device, D3DCompiler.Compile(src, "CSDivergenceAdv", "cs_5_0"));
            _divergenceCorrect = new ComputeShader(_device, D3DCompiler.Compile(src, "CSDivergenceCorrect", "cs_5_0"));

            _computeFactor = new ComputeShader(_device, D3DCompiler.Compile(src, "CSComputeFactor", "cs_5_0"));
            _density = new ComputeShader(_device, D3DCompiler.Compile(src, "CSDensity", "cs_5_0"));
            _count = new ComputeShader(_device, D3DCompiler.Compile(src, "CSCount", "cs_5_0"));

            _scanBlocks = new ComputeShader(_device, D3DCompiler.Compile(src, "CSScanBlocks", "cs_5_0"));
            _scanBlockSums = new ComputeShader(_device, D3DCompiler.Compile(src, "CSScanBlockSums", "cs_5_0"));
            _addOffsets = new ComputeShader(_device, D3DCompiler.Compile(src, "CSAddOffsets", "cs_5_0"));
            _scatter = new ComputeShader(_device, D3DCompiler.Compile(src, "CSScatter", "cs_5_0"));

            _particleBuffer = new Buffer(_device, new BufferDescription
            {
                SizeInBytes = bufferWidth,
                BindFlags = BindFlags.UnorderedAccess,
                Usage = ResourceUsage.Default,
                OptionFlags = ResourceOptionFlags.BufferStructured,
                StructureByteStride = _stride
            });

            _particleUav = new UnorderedAccessView(_device, _particleBuffer, new UnorderedAccessViewDescription
            {
                Format = SharpDX.DXGI.Format.Unknown,
                Dimension = UnorderedAccessViewDimension.Buffer,
                Buffer = new UnorderedAccessViewDescription.BufferResource
                { FirstElement = 0, ElementCount = Capacity, Flags = UnorderedAccessViewBufferFlags.None }
            });

            _gridConstantBuffer = new Buffer(_device, new BufferDescription
            {
                SizeInBytes = Marshal.SizeOf<GridConstants>(),
                BindFlags = BindFlags.ConstantBuffer,
                Usage = ResourceUsage.Default
            });

            _stagingBuffer = new Buffer(_device, new BufferDescription
            {
                SizeInBytes = bufferWidth,
                BindFlags = BindFlags.None,
                Usage = ResourceUsage.Staging,
                CpuAccessFlags = CpuAccessFlags.Read
            });
        }

        // Upload the initial particles
        public void Seed(Particle[] particles)
        {
            ParticleCount = Math.Min(particles.Length, _capacity);
            int bufferWidth = ParticleCount * _stride;

            ResourceRegion region = new ResourceRegion(0, 0, 0, bufferWidth, 1, 1);
            GCHandle handle = GCHandle.Alloc(particles, GCHandleType.Pinned);

            try
            {
                _context.UpdateSubresource(new DataBox(handle.AddrOfPinnedObject(), bufferWidth, bufferWidth), _particleBuffer, 0, region);
            }
            finally { handle.Free(); }
        }

        public void InitializeSolver(DomainBound[] bounds)
        {
            _solverConstantBuffer = new Buffer(_device, new BufferDescription
            {
                SizeInBytes = Marshal.SizeOf<SolverConstants>(),
                BindFlags = BindFlags.ConstantBuffer,
                Usage = ResourceUsage.Default,
            });

            int stride = Marshal.SizeOf<DomainBound>();
            _domainBounds?.Dispose(); _domainBoundsSrv?.Dispose();

            GCHandle h = GCHandle.Alloc(bounds, GCHandleType.Pinned);
            try
            {
                _domainBounds = new Buffer(_device, h.AddrOfPinnedObject(), new BufferDescription
                {
                    SizeInBytes = bounds.Length * stride,
                    BindFlags = BindFlags.ShaderResource,
                    OptionFlags = ResourceOptionFlags.BufferStructured,
                    StructureByteStride = stride,
                    Usage = ResourceUsage.Default,
                });
            }
            finally { h.Free(); }

            _domainBoundsSrv = new ShaderResourceView(_device, _domainBounds, new ShaderResourceViewDescription
            {
                Format = SharpDX.DXGI.Format.Unknown,
                Dimension = SharpDX.Direct3D.ShaderResourceViewDimension.Buffer,
                Buffer = new ShaderResourceViewDescription.BufferResource { FirstElement = 0, ElementCount = bounds.Length },
            });
        }

        public void UpdateSolverConstants(Vector3 gravity, float dt, float restitution)
        {
            SolverConstants cb = new SolverConstants
            {
                Gravity = gravity,
                Dt = dt,
                InvH = 1f / dt,
                InvH2 = 1f / (dt * dt),
                Restitution = restitution,
            };
            _context.UpdateSubresource(ref cb, _solverConstantBuffer);
        }

        public void InitializeGridHash(float h, float mass, float restDensity, int n)
        {
            TableSize = NextPow2((uint)Math.Max(1, 2 * n));
            uint numBlocks = (TableSize + 511) / 512;

            GridConstants cb = new GridConstants
            {
                CellSize = h,
                TableSize = TableSize,
                TotalCount = (uint)n,
                NumBlocks = numBlocks,
                Mass = mass,
                KernelK = 8f / ((float)Math.PI * h * h * h),
                RestDensity = restDensity
            };
            _context.UpdateSubresource(ref cb, _gridConstantBuffer);

            DisposeHashBuffers();
            (_cellCount, _cellCountUav) = CreateUintBuffer((int)TableSize);
            (_cellStart, _cellStartUav) = CreateUintBuffer((int)TableSize);
            (_blockSums, _blockSumsUav) = CreateUintBuffer((int)numBlocks);

            (_factor, _factorUav) = CreateUintBuffer(Capacity);
            (_cellIndex, _cellIndexUav) = CreateUintBuffer(Capacity);
            (_localOffset, _localOffsetUav) = CreateUintBuffer(Capacity);
            (_sortedIndices, _sortedIndicesUav) = CreateUintBuffer(Capacity);
        }

        public void BuildHash()
        {
            int numParticleGroups = (ParticleCount + THREADS - 1) / THREADS;
            int numBlocks = ((int)TableSize + SCAN_BLOCK - 1) / SCAN_BLOCK;

            // Clear
            _context.ClearUnorderedAccessView(_cellCountUav, new Int4(0, 0, 0, 0));

            // Set
            _context.ComputeShader.SetConstantBuffer(0, _gridConstantBuffer);
            _context.ComputeShader.SetUnorderedAccessView(0, _particleUav);
            _context.ComputeShader.SetUnorderedAccessView(1, _cellCountUav);
            _context.ComputeShader.SetUnorderedAccessView(2, _cellStartUav);
            _context.ComputeShader.SetUnorderedAccessView(3, _blockSumsUav);
            _context.ComputeShader.SetUnorderedAccessView(4, _cellIndexUav);
            _context.ComputeShader.SetUnorderedAccessView(5, _localOffsetUav);
            _context.ComputeShader.SetUnorderedAccessView(6, _sortedIndicesUav);

            // Count pass
            _context.ComputeShader.Set(_count);
            _context.Dispatch(numParticleGroups, 1, 1);

            // Scan pass
            _context.ComputeShader.Set(_scanBlocks);
            _context.Dispatch(numBlocks, 1, 1);

            // Scan sums pass
            _context.ComputeShader.Set(_scanBlockSums);
            _context.Dispatch(1, 1, 1);

            // Add block offsets pass
            _context.ComputeShader.Set(_addOffsets);
            _context.Dispatch(numBlocks, 1, 1);

            // Scatter pass
            _context.ComputeShader.Set(_scatter);
            _context.Dispatch(numParticleGroups, 1, 1);

            // Reset & unbind
            for (int s = 0; s <= 6; s++) _context.ComputeShader.SetUnorderedAccessView(s, null);
            _context.ComputeShader.Set(null);
        }

        private void DispatchParticles() => _context.Dispatch((ParticleCount + THREADS - 1) / THREADS, 1, 1);

        private void BindSolve(ComputeShader shader, bool needFactor)
        {
            _context.ComputeShader.Set(shader);

            _context.ComputeShader.SetConstantBuffer(0, _gridConstantBuffer);
            _context.ComputeShader.SetConstantBuffer(1, _solverConstantBuffer);

            _context.ComputeShader.SetUnorderedAccessView(0, _particleUav);
            _context.ComputeShader.SetUnorderedAccessView(1, _cellCountUav);
            _context.ComputeShader.SetUnorderedAccessView(2, _cellStartUav);
            _context.ComputeShader.SetUnorderedAccessView(6, _sortedIndicesUav);

            if (needFactor) _context.ComputeShader.SetUnorderedAccessView(7, _factorUav);
        }

        private void UnbindSolve()
        {
            for (int s = 0; s <= 7; s++) _context.ComputeShader.SetUnorderedAccessView(s, null);
            _context.ComputeShader.Set(null);
        }

        public void ApplyForces()
        {
            _context.ComputeShader.Set(_applyForces);

            _context.ComputeShader.SetConstantBuffer(0, _gridConstantBuffer);
            _context.ComputeShader.SetConstantBuffer(1, _solverConstantBuffer);
            _context.ComputeShader.SetUnorderedAccessView(0, _particleUav);

            DispatchParticles();

            _context.ComputeShader.SetUnorderedAccessView(0, null);
            _context.ComputeShader.Set(null);
        }

        public void IntegratePos()
        {
            _context.ComputeShader.Set(_integratePos);

            _context.ComputeShader.SetConstantBuffer(0, _gridConstantBuffer);
            _context.ComputeShader.SetConstantBuffer(1, _solverConstantBuffer);

            _context.ComputeShader.SetUnorderedAccessView(0, _particleUav);
            _context.ComputeShader.SetShaderResource(0, _domainBoundsSrv);

            DispatchParticles();

            _context.ComputeShader.SetUnorderedAccessView(0, null);
            _context.ComputeShader.SetShaderResource(0, null);

            _context.ComputeShader.Set(null);
        }

        public void DensityAdv() { BindSolve(_densityAdv, false); DispatchParticles(); UnbindSolve(); }
        public void PressureCorrect() { BindSolve(_pressureCorrect, true); DispatchParticles(); UnbindSolve(); }
        public void DivergenceAdv() { BindSolve(_divergenceAdv, false); DispatchParticles(); UnbindSolve(); }
        public void DivergenceCorrect() { BindSolve(_divergenceCorrect, true); DispatchParticles(); UnbindSolve(); }

        public void ComputeDensity()
        {
            _context.ComputeShader.Set(_density);
            _context.ComputeShader.SetConstantBuffer(0, _gridConstantBuffer);
            _context.ComputeShader.SetUnorderedAccessView(0, _particleUav);
            _context.ComputeShader.SetUnorderedAccessView(1, _cellCountUav);
            _context.ComputeShader.SetUnorderedAccessView(2, _cellStartUav);
            _context.ComputeShader.SetUnorderedAccessView(6, _sortedIndicesUav);

            _context.Dispatch((ParticleCount + THREADS - 1) / THREADS, 1, 1);

            _context.ComputeShader.SetUnorderedAccessView(0, null);
            _context.ComputeShader.SetUnorderedAccessView(1, null);
            _context.ComputeShader.SetUnorderedAccessView(2, null);
            _context.ComputeShader.SetUnorderedAccessView(6, null);
            _context.ComputeShader.Set(null);
        }

        public void ComputeFactor()
        {
            _context.ComputeShader.Set(_computeFactor);

            _context.ComputeShader.SetConstantBuffer(0, _gridConstantBuffer);
            _context.ComputeShader.SetUnorderedAccessView(0, _particleUav);
            _context.ComputeShader.SetUnorderedAccessView(1, _cellCountUav);
            _context.ComputeShader.SetUnorderedAccessView(2, _cellStartUav);
            _context.ComputeShader.SetUnorderedAccessView(6, _sortedIndicesUav);
            _context.ComputeShader.SetUnorderedAccessView(7, _factorUav);

            _context.Dispatch((ParticleCount + THREADS - 1) / THREADS, 1, 1);

            _context.ComputeShader.SetUnorderedAccessView(0, null);
            _context.ComputeShader.SetUnorderedAccessView(1, null);
            _context.ComputeShader.SetUnorderedAccessView(2, null);
            _context.ComputeShader.SetUnorderedAccessView(6, null);
            _context.ComputeShader.SetUnorderedAccessView(7, null);

            _context.ComputeShader.Set(null);
        }

        // Copy the current GPU particles back to a CPU array
        public void ReadBack(Particle[] dst)
        {
            int bufferWidth = ParticleCount * _stride;
            ResourceRegion region = new ResourceRegion(0, 0, 0, bufferWidth, 1, 1);

            _context.CopySubresourceRegion(_particleBuffer, 0, region, _stagingBuffer, 0, 0, 0, 0);
            DataBox b = _context.MapSubresource(_stagingBuffer, 0, MapMode.Read, MapFlags.None);

            try 
            { 
                SharpDX.Utilities.Read(b.DataPointer, dst, 0, ParticleCount); 
            }
            finally { _context.UnmapSubresource(_stagingBuffer, 0); }
        }

        public uint[] ReadBackUint(Buffer src, int count)
        {
            using Buffer staging = new Buffer(_device, new BufferDescription
            {
                SizeInBytes = count * sizeof(uint),
                Usage = ResourceUsage.Staging,
                CpuAccessFlags = CpuAccessFlags.Read,
                BindFlags = BindFlags.None,
            });
            ResourceRegion region = new ResourceRegion(0, 0, 0, count * sizeof(uint), 1, 1);
            _context.CopySubresourceRegion(src, 0, region, staging, 0, 0, 0, 0);

            DataBox box = _context.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
            uint[] dst = new uint[count];

            try { SharpDX.Utilities.Read(box.DataPointer, dst, 0, count); }
            finally { _context.UnmapSubresource(staging, 0); }
            return dst;
        }

        public float[] ReadBackFloat(Buffer src, int count)
        {
            using Buffer staging = new Buffer(_device, new BufferDescription
            {
                SizeInBytes = count * sizeof(float),
                Usage = ResourceUsage.Staging,
                CpuAccessFlags = CpuAccessFlags.Read,
                BindFlags = BindFlags.None,
            });
            ResourceRegion region = new ResourceRegion(0, 0, 0, count * sizeof(float), 1, 1);
            _context.CopySubresourceRegion(src, 0, region, staging, 0, 0, 0, 0);

            DataBox box = _context.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
            float[] dst = new float[count];
            try { SharpDX.Utilities.Read(box.DataPointer, dst, 0, count); }
            finally { _context.UnmapSubresource(staging, 0); }
            return dst;
        }

        private (Buffer, UnorderedAccessView) CreateUintBuffer(int count)
        {
            Buffer buf = new Buffer(_device, new BufferDescription
            {
                SizeInBytes = count * sizeof(uint),
                BindFlags = BindFlags.UnorderedAccess,
                OptionFlags = ResourceOptionFlags.BufferStructured,
                StructureByteStride = sizeof(uint),
                Usage = ResourceUsage.Default,
            });

            UnorderedAccessView uav = new UnorderedAccessView(_device, buf, new UnorderedAccessViewDescription
            {
                Format = SharpDX.DXGI.Format.Unknown,
                Dimension = UnorderedAccessViewDimension.Buffer,
                Buffer = new UnorderedAccessViewDescription.BufferResource
                { FirstElement = 0, ElementCount = count, Flags = UnorderedAccessViewBufferFlags.None },
            });

            return (buf, uav);
        }

        private static uint NextPow2(uint v)
        {
            v--; v |= v >> 1; v |= v >> 2; v |= v >> 4; v |= v >> 8; v |= v >> 16; v++;
            return v == 0 ? 1u : v;
        }

        public void DisposeHashBuffers()
        {
            _cellCount?.Dispose();
            _cellCountUav?.Dispose();

            _cellStart?.Dispose();
            _cellStartUav?.Dispose();

            _blockSums?.Dispose();
            _blockSumsUav?.Dispose();

            _factor?.Dispose();
            _factorUav?.Dispose();

            _cellIndex?.Dispose();
            _cellIndexUav?.Dispose();

            _localOffset?.Dispose();
            _localOffsetUav?.Dispose();

            _sortedIndices?.Dispose();
            _sortedIndicesUav?.Dispose();
        }

        public void Dispose()
        {
            DisposeHashBuffers();

            _applyForces?.Dispose(); 
            _integratePos?.Dispose();
            _densityAdv?.Dispose(); 
            _pressureCorrect?.Dispose();

            _divergenceAdv?.Dispose();
            _divergenceCorrect?.Dispose();
            _solverConstantBuffer?.Dispose();
            _domainBoundsSrv?.Dispose(); 
            _domainBounds?.Dispose();

            _computeFactor?.Dispose();
            _count?.Dispose();
            _density?.Dispose();

            _stagingBuffer?.Dispose();
            _gridConstantBuffer?.Dispose();
            _particleUav?.Dispose();
            _particleBuffer?.Dispose();

            _addOffsets?.Dispose();
            _scanBlockSums?.Dispose();
            _scanBlocks?.Dispose();
            _scatter?.Dispose();
        }

    }

}
