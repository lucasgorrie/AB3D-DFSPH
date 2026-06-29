using Ab3d.DirectX.Common;
using System.Numerics;
using System.Windows;
using DxDirectionalLight = Ab3d.DirectX.Lights.DirectionalLight;


namespace AB3D_DFSPH
{

    public partial class MainWindow : Window
    {

        public MainWindow()
        {
            InitializeComponent();

            DirectXViewportView.DXSceneInitialized += SetSceneLighting;
        }

        private void SetSceneLighting(object? sender, EventArgs e)
        {
            var lights = DirectXViewportView.DXScene.Lights;

            // Key: overhead slightly front-left, slightly red — clean sharp highlights on metallic surfaces
            lights.Add(new DxDirectionalLight(new Vector3(0.6f, -1.0f, -0.2f))
            {
                DiffuseColor = new Color3(0.9f, 0.82f, 0.82f),
                SpecularColor = new Color3(0.9f, 0.82f, 0.82f),
            });

            // Fill: low from back-right, cool blue — lifts shadow detail on dark anodised surfaces
            lights.Add(new DxDirectionalLight(new Vector3(0.5f, 0.6f, 0.5f))
            {
                DiffuseColor = new Color3(0.45f, 0.50f, 0.65f),
                SpecularColor = new Color3(0.45f, 0.50f, 0.65f),
            });

            // Rim: cool blue from behind — separates robot silhouette and adds depth to metal edges
            lights.Add(new DxDirectionalLight(new Vector3(0.1f, 0.2f, 1.0f))
            {
                DiffuseColor = new Color3(0.28f, 0.32f, 0.45f),
                SpecularColor = new Color3(0.28f, 0.32f, 0.45f),
            });
        }

    }

}
