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

        private FluidCompute? _compute;
        private DXViewportView _viewportView;
        private Stopwatch _renderTimer;

        private Device? _device;
        private DeviceContext? _context;

        private PixelsVisual3D? _pixels;
        private Vector3[] _renderPositions = Array.Empty<Vector3>();

        private Particle[] _seedParticles = Array.Empty<Particle>();
        private Particle[] _cpuParticles = Array.Empty<Particle>();

        public DFSPHSimulation(DXViewportView viewportView)
        {
            _renderTimer = new Stopwatch();
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
            IsRunning = true;

            RunFluidTest();
        }

        private void RunFluidTest()
        {
            if (_compute is null) return;

            // Init particle compute shaders
            int n = 50_000;
            _compute.Initialize(n);

            // Set param fields
            _boxMin = new Vector3(-0.5f, 0f, -0.5f);
            _boxMax = new Vector3(0.5f, 1.5f, 0.5f);
            const float spacing = 0.02f;

            // Space initial particles
            List<Particle> seed = new List<Particle>();
            for (float x = -0.2f; x <= 0.2f; x += spacing)
                for (float y = 1.0f; y <= 1.4f; y += spacing)
                    for (float z = -0.2f; z <= 0.2f; z += spacing)
                        seed.Add(new Particle { Position = new Vector3(x, y, z) });

            // Record initial state and seed fluid compute
            _seedParticles = seed.ToArray();
            _compute.Seed(_seedParticles);

            _renderPositions = seed.Select(p => p.Position).ToArray();
            _cpuParticles = new Particle[_compute.ParticleCount];

            // Set initial visuals
            _pixels = new PixelsVisual3D(_renderPositions)
            {
                PixelColor = new Color() { ScR = 0.3f, ScG = 0.55f, ScB = 1.0f, ScA = 1.0f },
                PixelSize = 4.0f
            };
            _viewportView.Viewport3D.Children.Add(_pixels);

            // Subscribe to rendering
            _viewportView.SceneUpdating += OnRender;
        }

        // Render delegate & temporary fields
        private Vector3 _boxMin;
        private Vector3 _boxMax;
        private void OnRender(object? sender, EventArgs e)
        {
            if (!IsRunning || _compute is null) return;

            // Determine frame dt
            if (!_renderTimer.IsRunning)
            {
                _renderTimer.Start();
                return;
            }
            float dt = (float)_renderTimer.Elapsed.TotalSeconds;
            _renderTimer.Restart();

            Step(dt);
        }

        private void UpdateRender()
        {
            if (_compute is null || _pixels is null) return;

            // Read gpu compute back into cpuParticles and update visuals
            _compute.ReadBack(_cpuParticles);
            for (int i = 0; i < _renderPositions.Length; i++)
                _renderPositions[i] = _cpuParticles[i].Position;
            _pixels.UpdatePositions();
        }

        #region State Control

        public void Start()
        {
            IsRunning = true;
            _renderTimer.Restart();
        }

        public void Pause()
        {
            IsRunning = false;
            _renderTimer.Stop();
        }

        public void Step()
        {
            Step(MaxTimeStep);
        }

        public void Step(float dt = -1)
        {
            if (_compute is null) return;
            if (dt <= 0) dt = MaxTimeStep;

            // Compute particle substeps
            int subs = Math.Max(1, (int)MathF.Ceiling(dt / MaxTimeStep));
            float subDt = dt / subs;
            for (int s = 0; s < subs; s++)
                _compute.Step(subDt, Gravity, _boxMin, _boxMax);

            UpdateRender();
        }

        public void Reset()
        {
            if (_compute is null) return;

            _compute.Seed(_seedParticles);
            UpdateRender();
        }

        #endregion

        public void Dispose()
        {
            if (_pixels is not null) _viewportView.Viewport3D.Children.Remove(_pixels);

            _viewportView.DXSceneInitialized -= OnDXSceneInitialized;
            _viewportView.SceneUpdating -= OnRender;
            _compute?.Dispose();
            _context?.Dispose();
            _device?.Dispose();
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
