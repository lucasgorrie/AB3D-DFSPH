using System.Runtime.InteropServices;


namespace SPH
{

    internal static class D3DCompiler
    {

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate IntPtr GetBufferPointerFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate IntPtr GetBufferSizeFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate uint ReleaseFn(IntPtr self);

        [DllImport("d3dcompiler_47.dll", EntryPoint = "D3DCompile")]
        private static extern int D3DCompile_Native(IntPtr pSrcData, IntPtr SrcDataSize,
                                                    [MarshalAs(UnmanagedType.LPStr)] string? pSourceName, IntPtr pDefines, IntPtr pInclude,
                                                    [MarshalAs(UnmanagedType.LPStr)] string pEntrypoint, [MarshalAs(UnmanagedType.LPStr)] string pTarget,
                                                    uint Flags1, uint Flags2, out IntPtr ppCode, out IntPtr ppErrorMsgs);

        // Read bytes from an ID3DBlob*, then release it.  Returns null when blob == 0.
        private static byte[]? ConsumeBlobBytes(IntPtr blob)
        {
            if (blob == IntPtr.Zero) return null;

            IntPtr vtbl = Marshal.ReadIntPtr(blob);
            IntPtr getPtr = Marshal.ReadIntPtr(vtbl, 3 * IntPtr.Size);   // GetBufferPointer
            IntPtr getSize = Marshal.ReadIntPtr(vtbl, 4 * IntPtr.Size);  // GetBufferSize
            IntPtr relPtr = Marshal.ReadIntPtr(vtbl, 2 * IntPtr.Size);   // Release

            var bufferPointer = Marshal.GetDelegateForFunctionPointer<GetBufferPointerFn>(getPtr);
            var bufferSize = Marshal.GetDelegateForFunctionPointer<GetBufferSizeFn>(getSize);
            var release = Marshal.GetDelegateForFunctionPointer<ReleaseFn>(relPtr);

            try
            {
                IntPtr dataPtr = bufferPointer(blob);
                int count = (int)bufferSize(blob);
                if (dataPtr == IntPtr.Zero || count <= 0) return null;

                byte[] result = new byte[count];
                Marshal.Copy(dataPtr, result, 0, count);
                return result;
            }
            finally
            {
                release(blob);
            }
        }

        public static byte[] Compile(string hlsl, string entry, string target)
        {
            byte[] src = System.Text.Encoding.UTF8.GetBytes(hlsl);
            var pin = GCHandle.Alloc(src, GCHandleType.Pinned);
            IntPtr codeBlob = IntPtr.Zero;
            IntPtr errorBlob = IntPtr.Zero;

            try
            {
                int hr = D3DCompile_Native(pin.AddrOfPinnedObject(), new IntPtr(src.Length), null, IntPtr.Zero, IntPtr.Zero,
                                           entry, target, 0, 0, out codeBlob, out errorBlob);

                string? errText = null;
                if (errorBlob != IntPtr.Zero)
                {
                    byte[]? errBytes = ConsumeBlobBytes(errorBlob);
                    errorBlob = IntPtr.Zero; // consumed
                    errText = errBytes is not null ? System.Text.Encoding.UTF8.GetString(errBytes).TrimEnd('\0') : null;
                }

                if (hr < 0) throw new InvalidOperationException($"HLSL compile failed [{target}/{entry}]:\n{errText}");

                byte[]? data = ConsumeBlobBytes(codeBlob);
                codeBlob = IntPtr.Zero; // consumed

                return data ?? throw new InvalidOperationException($"D3DCompile returned empty bytecode for [{entry}].");
            }
            finally
            {
                pin.Free();

                // Safety release in case an exception was thrown
                ConsumeBlobBytes(codeBlob);
                ConsumeBlobBytes(errorBlob);
            }
        }
    }

}
