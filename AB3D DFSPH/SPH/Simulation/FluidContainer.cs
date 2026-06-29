using Ab3d.DirectX;
using System.Numerics;


namespace SPH.Simulation
{

    public sealed class FluidContainer
    {

        public FluidContainer(SceneNode containerNode, FluidProperties fluid)
        {
            ContainerNode = containerNode;
            Fluid = fluid;
        }

        public FluidContainer(SceneNode containerNode, FluidProperties fluid, float fillFraction)
        {
            ContainerNode = containerNode;
            Fluid = fluid;
            FillFraction = fillFraction;
        }

        #region Properties

        public FluidProperties Fluid { get; set; }
        public SceneNode ContainerNode { get; }

        public Vector3 BoxMin { get; set; }
        public Vector3 BoxMax { get; set; }

        public int BufferOffset { get; set; }
        public int ParticleCount { get; internal set; }
        public float FillFraction { get; set; }
        public string? Name { get; set; }

        #endregion

    }

}
