using Ab3d.DirectX.Common;
using SPH;
using System.Numerics;
using System.Windows;
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
