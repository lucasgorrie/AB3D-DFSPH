using System.Numerics;
using System.Runtime.InteropServices;


namespace SPH.Compute
{

    [StructLayout(LayoutKind.Sequential)]
    public struct Particle
    {
        public Vector3 Position; public uint DomainId;
        public Vector3 Velocity; public uint MaterialId;
    }  // stride = 32 bytes

    [StructLayout(LayoutKind.Sequential)]
    public struct SimConstants
    {
        public Vector3 Gravity; public float Dt;
        public Vector3 BoxMin; public float Restitution;
        public Vector3 BoxMax; public uint ParticleCount;
    }  // stride = 48 bytes

}
