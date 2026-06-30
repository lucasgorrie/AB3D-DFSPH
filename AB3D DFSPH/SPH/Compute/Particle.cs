using System.Numerics;
using System.Runtime.InteropServices;


namespace SPH.Compute
{

    [StructLayout(LayoutKind.Sequential)]
    public struct Particle
    {
        public Vector3 Position; public uint DomainId;
        public Vector3 Velocity; public uint MaterialId;
        public float Density; public uint GroupId;
        public float DensityAdv;
    }  // Stride = 44 bytes

    [StructLayout(LayoutKind.Sequential)]
    public struct GridConstants
    {
        public float CellSize; public uint TableSize;
        public uint TotalCount; public uint NumBlocks;

        public float Mass; public float KernelK;
        public float RestDensity; public uint Padding;
    }  // Stride = 32 bytes

    [StructLayout(LayoutKind.Sequential)]
    public struct SolverConstants
    {
        public Vector3 Gravity; public float Dt;

        public float InvH; public float InvH2;
        public float Restitution; public float Padding;
    }  // Stride = 32 bytes

    [StructLayout(LayoutKind.Sequential)]
    public struct DomainBound
    {
        public Vector4 Min; 
        public Vector4 Max;
    }  // Stride = 32 bytes

}
