using Ab3d.DirectX;


namespace SPH.Simulation
{

    public sealed class FluidContainer
    {

        public FluidContainer(SceneNode containerNode, FluidProperties fluid)
        {
            ContainerNode = containerNode;
            Fluid = fluid;
        }

        public FluidProperties Fluid { get; set; }
        public SceneNode ContainerNode { get; }

        public float FillFraction { get; set; }
        public string? Name { get; set; }

    }

}
