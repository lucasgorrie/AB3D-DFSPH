

namespace SPH.Simulation
{

    public sealed class FluidProperties
    {

        public float RestDensity { get; set; } = 1000f;
        public float ParticleRadius { get; set; } = 0.025f;
        public float Viscosity { get; set; } = 0.01f;
        public float SurfaceTension { get; set; } = 0f;

        public float ParticleSpacing => 2f * ParticleRadius;
        public float SupportRadius => 4f * ParticleRadius;
        public float ParticleMass => RestDensity * ParticleSpacing * ParticleSpacing * ParticleSpacing;

        public static FluidProperties Water => new();

    }

}
