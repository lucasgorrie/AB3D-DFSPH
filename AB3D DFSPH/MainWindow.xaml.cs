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
            Ab3d.Meshes.BoxMesh3D boxMesh0 = new Ab3d.Meshes.BoxMesh3D(new Point3D(-3, -0.25, 0), new Size3D(0.5, 0.5, 0.5), 8, 8, 8);
            GeometryModel3D boxModel0 = new GeometryModel3D() { Geometry = boxMesh0.Geometry };

            Ab3d.Meshes.BoxMesh3D boxMesh1 = new Ab3d.Meshes.BoxMesh3D(new Point3D(0, 0, 0), new Size3D(1, 1, 1), 8, 8, 8);
            GeometryModel3D boxModel1 = new GeometryModel3D() { Geometry = boxMesh1.Geometry };

            Ab3d.Meshes.BoxMesh3D boxMesh2 = new Ab3d.Meshes.BoxMesh3D(new Point3D(3, 0.5, 0), new Size3D(2, 2, 2), 8, 8, 8);
            GeometryModel3D boxModel2 = new GeometryModel3D() { Geometry = boxMesh2.Geometry };

            SceneNode testNode0 = SceneNodeFactory.CreateFromModel3D(boxModel0, null, DirectXViewportView.DXScene);
            SceneNode testNode1 = SceneNodeFactory.CreateFromModel3D(boxModel1, null, DirectXViewportView.DXScene);
            SceneNode testNode2 = SceneNodeFactory.CreateFromModel3D(boxModel2, null, DirectXViewportView.DXScene);

            DirectXViewportView.DXScene.RootNode.AddChild(testNode0);
            DirectXViewportView.DXScene.RootNode.AddChild(testNode1);
            DirectXViewportView.DXScene.RootNode.AddChild(testNode2);

            FluidContainer testContainer0 = new FluidContainer(testNode0, FluidProperties.Water, 0.5F);
            FluidContainer testContainer1 = new FluidContainer(testNode1, FluidProperties.Water, 0.5F);
            FluidContainer testContainer2 = new FluidContainer(testNode2, FluidProperties.Water, 0.5F);

            _fluidSimulation.AddContainer(testContainer0);
            _fluidSimulation.AddContainer(testContainer1);
            _fluidSimulation.AddContainer(testContainer2);
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
