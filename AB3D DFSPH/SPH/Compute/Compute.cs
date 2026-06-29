using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D11;
using Buffer = SharpDX.Direct3D11.Buffer;


namespace SPH.Compute
{

    public class Compute : IDisposable
    {

        private readonly Device _device;
        private readonly DeviceContext _context;

        private readonly string _particleComputeShader;

        public Compute(Device device, DeviceContext context)
        {
            _device = device;
            _context = context;

            _particleComputeShader = File.ReadAllText("SPH\\Compute\\particles.hlsl");
        }

        public bool RunSelfTest(int n = 256)
        {
            // Compile HLSL
            byte[] bytecode = D3DCompiler.Compile(_particleComputeShader, "CSMain", "cs_5_0");
            using ComputeShader shader = new ComputeShader(_device, bytecode);

            using Buffer dataBuffer = new Buffer(_device, new BufferDescription
            {
                SizeInBytes = n * sizeof(float),
                BindFlags = BindFlags.UnorderedAccess,  // shader writes via a UAV
                Usage = ResourceUsage.Default,          // lives in GPU memory
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.BufferStructured,
                StructureByteStride = sizeof(float),    // one float per element
            });

            // UAV
            using UnorderedAccessView uav = new UnorderedAccessView(_device, dataBuffer, new UnorderedAccessViewDescription
            {
                Format = SharpDX.DXGI.Format.Unknown,  // Unknown = structured buffer
                Dimension = UnorderedAccessViewDimension.Buffer,
                Buffer = new UnorderedAccessViewDescription.BufferResource
                {
                    FirstElement = 0,
                    ElementCount = n,
                    Flags = UnorderedAccessViewBufferFlags.None,
                },
            });

            // CPU-readable staging buffer
            using Buffer stagingBuffer = new Buffer(_device, new BufferDescription
            {
                SizeInBytes = n * sizeof(float),
                BindFlags = BindFlags.None,
                Usage = ResourceUsage.Staging,
                CpuAccessFlags = CpuAccessFlags.Read,
            });

            // Bind shader + UAV, dispatch enough 64-thread groups
            _context.ComputeShader.Set(shader);
            _context.ComputeShader.SetUnorderedAccessView(0, uav);
            _context.Dispatch((n + 63) / 64, 1, 1);

            // Unbind
            _context.ComputeShader.SetUnorderedAccessView(0, null);
            _context.ComputeShader.Set(null);

            // Copy gpu buffer into staging buffer, map into CPU memory
            _context.CopyResource(dataBuffer, stagingBuffer);
            DataBox box = _context.MapSubresource(stagingBuffer, 0, MapMode.Read, MapFlags.None);

            // Copy into float array
            float[] results = new float[n];
            Marshal.Copy(box.DataPointer, results, 0, n);
            _context.UnmapSubresource(stagingBuffer, 0);

            // Verify Output[i] == i*2
            for (int i = 0; i < n; i++)
                if (results[i] != i * 2f)
                {
                    Debug.WriteLine($"[Compute] FAILED at {i}: got {results[i]}, expected {i * 2}");
                    return false;
                }
            Debug.WriteLine($"[Compute] OK (n={n}): {results[0]},{results[1]},{results[2]},...,{results[n - 1]}");
            return true;
        }

        public void Dispose()
        {

        }

    }

}
