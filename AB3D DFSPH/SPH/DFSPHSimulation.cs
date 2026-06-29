using Ab3d.DirectX.Common;
using Ab3d.DirectX.Controls;
using Ab3d.Visuals;
using SharpDX.Direct3D11;
using SPH.Compute;
using SPH.Simulation;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Media;


namespace SPH
{

    public sealed class DFSPHSimulation : IDisposable
    {

        private FluidCompute _compute;
        private DXViewportView _viewportView;

        private Device? _device;
        private DeviceContext? _context;

        private PixelsVisual3D? _pixels;
        private Vector3[] _renderPositions = Array.Empty<Vector3>();

        public DFSPHSimulation(DXViewportView viewportView)
        {
            _domainGroups = new List<DomainGroup>();
            _viewportView = viewportView;

            if (_viewportView.DXScene?.IsInitialized == true) OnDXSceneInitialized(_viewportView, EventArgs.Empty);
            else _viewportView.DXSceneInitialized += OnDXSceneInitialized;
        }

        #region Properties

        public Vector3 Gravity { get; set; } = new(0, -9.81f, 0);

        private readonly List<DomainGroup> _domainGroups;
        public IReadOnlyList<DomainGroup> DomainGroups
        { 
            get => _domainGroups;
        }

        public ParticleRenderMode RenderMode { get; set; } = ParticleRenderMode.Points;

        public int SubStepsPerFrame { get; set; } = 1;
        public int MinIterations { get; set; } = 2;
        public int MaxIterations { get; set; } = 100;
        public float MaxTimeStep { get; set; } = 1f / 120f;   // Should be CFL-capped
        public float MaxDensityError { get; set; } = 0.01f;

        public bool IsRunning { get; private set; }
        public int ParticleCount { get; private set; }

        #endregion

        #region Domain Control

        public void AddDomainGroup(DomainGroup domainGroup)
        {
            _domainGroups.Add(domainGroup);
        }

        public void AddContainer(FluidContainer container)
        {
            _domainGroups.Add(new DomainGroup(container));
        }

        /// <summary>
        /// Joins all FluidContainer domains in <paramref name="domains"/> into the first domain.
        /// All other domains are kept & remain named, but are empty.
        /// </summary>
        /// <returns> A reference to the head domain which all FluidContainers were grouped into </returns>
        public DomainGroup JoinDomains(IEnumerable<DomainGroup> domains)
        {
            List<DomainGroup> copy = domains.ToList();  // avoid collection modified during enumeration error
            DomainGroup head = copy[0];

            foreach (DomainGroup domainGroup in copy)
            {
                if (domainGroup == head) continue;

                foreach (FluidContainer domain in domainGroup.Domains.ToList())
                {
                    head.AddDomain(domain);
                    domainGroup.RemoveDomain(domain);
                }
            }

            return head;
        }

        /// <summary>
        /// Separates all DomainGroups in <paramref name="domains"/> into single-domain groups
        /// </summary>
        public void SeparateDomains(IEnumerable<DomainGroup> domains)
        {
            foreach (DomainGroup domainGroup in domains.ToList())  // ToList to avoid collection modified during enumeration error
            {
                int domainsCount = domainGroup.Domains.Count;
                if (domainsCount < 2) continue;

                // Move domains into their own groups
                foreach (FluidContainer domain in domainGroup.Domains.Skip(1).ToList())  // all but the first
                {
                    domainGroup.RemoveDomain(domain);
                    AddContainer(domain);
                }
            }
        }

        /// <summary>
        /// Separates all DomainGroups in this DFSPHSimulator into single-domain groups
        /// </summary>
        public void SeparateAllDomains()
        {
            SeparateDomains(DomainGroups);
        }

        #endregion

        // Allocate GPU buffers, seed particles, insert the rendering step
        private void Initialize()
        {
            if (_device != null) return;

            // Get pointer to DXDevice and increment its reference count
            IntPtr p = _viewportView.DXScene.DXDevice.Device.NativePointer;
            Marshal.AddRef(p);

            // Get a SharpDX handle to the device and its context from the pointer
            _device = new Device(p);
            _context = _device.ImmediateContext;

            _compute = new FluidCompute(_device, _context);
            RunComputeTest();
        }

        private void RunComputeTest()
        {
            _compute.Initialize(20000);

            Vector3 boxMin = new Vector3(-0.5f, 0f, -0.5f);
            Vector3 boxMax = new Vector3(0.5f, 1.5f, 0.5f);
            const float spacing = 0.04f;

            List<Particle> seed = new List<Particle>();
            for (float x = -0.2f; x <= 0.2f; x += spacing)
                for (float y = 1.0f; y <= 1.4f; y += spacing)
                    for (float z = -0.2f; z <= 0.2f; z += spacing)
                        seed.Add(new Particle { Position = new Vector3(x, y, z) });

            _compute.Seed(seed.ToArray());
            _renderPositions = seed.Select(p => p.Position).ToArray();

            _pixels = new PixelsVisual3D(_renderPositions)
            {
                PixelColor = new Color() { ScR = 0.3f, ScG = 0.55f, ScB = 1.0f, ScA = 1.0f },
                PixelSize = 4.0f
            };
            _viewportView.Viewport3D.Children.Add(_pixels);
        }

        #region State Control

        public void Start()
        {

        }

        public void Pause()
        {

        }

        public void Step()
        {

        }

        public void Reset()
        {

        }

        #endregion

        public void Dispose()
        {
            _context?.Dispose();
            _device?.Dispose();
            _viewportView.DXSceneInitialized -= OnDXSceneInitialized;
        }

        #region Events & Event Handling

        public event EventHandler? Stepped;

        private void OnDXSceneInitialized(object? sender, EventArgs e)
        {
            Initialize();
        }

        #endregion

    }

    public enum ParticleRenderMode { Points, Spheres }

}
