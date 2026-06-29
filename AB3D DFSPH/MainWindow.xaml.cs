using Ab3d.DirectX;
using Ab3d.DirectX.Common;
using Ab3d.DirectX.Models;
using Ab3d.Visuals;
using SPH;
using SPH.Simulation;
using System.Numerics;
using System.Windows;
using System.Windows.Media.Media3D;
using DxDirectionalLight = Ab3d.DirectX.Lights.DirectionalLight;


namespace AB3D_DFSPH
{

    public partial class MainWindow : Window
    {

        private DFSPHSimulation _fluidSimulation;

        public MainWindow()
        {
            InitializeComponent();
            _fluidSimulation = new DFSPHSimulation(DirectXViewportView);

            DirectXViewportView.DXSceneInitialized += SetSceneLighting;
            DirectXViewportView.DXSceneInitialized += SetupFluidDemo;
        }

        private void SetupFluidDemo(object? sender, EventArgs e)
        {
            Ab3d.Meshes.BoxMesh3D boxMesh = new Ab3d.Meshes.BoxMesh3D(new Point3D(0, 0, 0), new Size3D(1, 1, 1), 8, 8, 8);
            GeometryModel3D boxModel = new GeometryModel3D() { Geometry = boxMesh.Geometry };

            SceneNode testNode = SceneNodeFactory.CreateFromModel3D(boxModel, null, DirectXViewportView.DXScene);
            DirectXViewportView.Viewport3D.Children.Add(new SceneNodeVisual3D(testNode));

            FluidContainer testContainer = new FluidContainer(testNode, FluidProperties.Water, 0.5F);

            _fluidSimulation.AddContainer(testContainer);
            _fluidSimulation.Start();
        }

        private void SetSceneLighting(object? sender, EventArgs e)
        {
            var lights = DirectXViewportView.DXScene.Lights;

            // Key
            lights.Add(new DxDirectionalLight(new Vector3(0.6f, -1.0f, -0.2f))
            {
                DiffuseColor = new Color3(0.9f, 0.82f, 0.82f),
                SpecularColor = new Color3(0.9f, 0.82f, 0.82f),
            });

            // Fill
            lights.Add(new DxDirectionalLight(new Vector3(0.5f, 0.6f, 0.5f))
            {
                DiffuseColor = new Color3(0.45f, 0.50f, 0.65f),
                SpecularColor = new Color3(0.45f, 0.50f, 0.65f),
            });

            // Rim
            lights.Add(new DxDirectionalLight(new Vector3(0.1f, 0.2f, 1.0f))
            {
                DiffuseColor = new Color3(0.28f, 0.32f, 0.45f),
                SpecularColor = new Color3(0.28f, 0.32f, 0.45f),
            });
        }

    }

}
