using SharpDX;
using SharpDX.Direct3D11;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Buffer = SharpDX.Direct3D11.Buffer;


namespace SPH.Compute
{

    public class FluidCompute : IDisposable
    {

        private readonly Device _device;
        private readonly DeviceContext _context;

        private ComputeShader _integrate = null!;
        private Buffer _particleBuffer = null!;
        private UnorderedAccessView _particleUav = null!;
        private Buffer _constantBuffer = null!;
        private Buffer _stagingBuffer = null!;

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

        #endregion

        // Compile shaders & allocate memory to buffers
        public void Initialize(int capacity)
        {
            Capacity = capacity;

            string shaderPath = Path.Combine(AppContext.BaseDirectory, "SPH", "Compute", "particles.hlsl");
            byte[] code = D3DCompiler.Compile(File.ReadAllText(shaderPath), "CSIntegrate", "cs_5_0");

            _integrate = new ComputeShader(_device, code);
            int bufferWidth = Capacity * _stride;

            _particleBuffer = new Buffer(_device, new BufferDescription
            {
                SizeInBytes = bufferWidth,
                BindFlags = BindFlags.UnorderedAccess,
                Usage = ResourceUsage.Default,
                OptionFlags = ResourceOptionFlags.BufferStructured,
                StructureByteStride = _stride,
            });

            _particleUav = new UnorderedAccessView(_device, _particleBuffer, new UnorderedAccessViewDescription
            {
                Format = SharpDX.DXGI.Format.Unknown,
                Dimension = UnorderedAccessViewDimension.Buffer,
                Buffer = new UnorderedAccessViewDescription.BufferResource
                { FirstElement = 0, ElementCount = Capacity, Flags = UnorderedAccessViewBufferFlags.None },
            });

            _constantBuffer = new Buffer(_device, new BufferDescription
            {
                SizeInBytes = Marshal.SizeOf<SimConstants>(),
                BindFlags = BindFlags.ConstantBuffer,
                Usage = ResourceUsage.Default,
            });

            _stagingBuffer = new Buffer(_device, new BufferDescription
            {
                SizeInBytes = bufferWidth,
                BindFlags = BindFlags.None,
                Usage = ResourceUsage.Staging,
                CpuAccessFlags = CpuAccessFlags.Read,
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

        public void Step(float dt, Vector3 gravity, Vector3 boxMin, Vector3 boxMax, float restitution = 0.3f)
        {
            SimConstants cb = new SimConstants
            {
                Gravity = gravity,
                Dt = dt,
                BoxMin = boxMin,
                Restitution = restitution,
                BoxMax = boxMax,
                ParticleCount = (uint)ParticleCount,
            };
            _context.UpdateSubresource(ref cb, _constantBuffer);

            _context.ComputeShader.Set(_integrate);
            _context.ComputeShader.SetUnorderedAccessView(0, _particleUav);
            _context.ComputeShader.SetConstantBuffer(0, _constantBuffer);
            _context.Dispatch((ParticleCount + 63) / 64, 1, 1);

            _context.ComputeShader.SetUnorderedAccessView(0, null);   // unbind for the next upload/readback
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

        public void Dispose()
        {
            _stagingBuffer?.Dispose();
            _constantBuffer?.Dispose();
            _particleUav?.Dispose();
            _particleBuffer?.Dispose();
            _integrate?.Dispose();
        }

    }

}
