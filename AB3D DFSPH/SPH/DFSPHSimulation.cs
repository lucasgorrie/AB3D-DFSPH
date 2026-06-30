using Ab3d.DirectX;
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

        private FluidCompute? _compute;
        private DXViewportView _viewportView;
        private Stopwatch _renderTimer;

        private Device? _device;
        private DeviceContext? _context;

        private PixelsVisual3D? _pixels;
        private Vector3[] _renderPositions = Array.Empty<Vector3>();
        private Color4[] _renderColours = Array.Empty<Color4>();

        private Particle[] _seedParticles = Array.Empty<Particle>();
        private Particle[] _cpuParticles = Array.Empty<Particle>();

        private float _particleDiameter;  // Will need to be expanded to support multiple Fluid configurations across containers
        private float _vMax;
        private bool _seeded;

        public const int MAXCAPACITY = 100_000;
        private const float CFLFACTOR = 0.2f;

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

        public int PressureIterations { get; set; } = 6;
        public int DivergenceIterations { get; set; } = 6;
        public int MaxSubsteps { get; set; } = 10;

        public float MaxTimeStep { get; set; } = 1f / 120f;
        public float MaxDensityError { get; set; } = 0.01f;

        public float Restitution { get; set; } = 0.4f;

        public bool IsRunning { get; private set; }
        public int ParticleCount => _domainGroups.SelectMany(g => g.Domains).Sum(c => c.ParticleCount);

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

            // Init particle compute shaders
            _compute.Initialize(MAXCAPACITY);

            // Subscribe to rendering
            _viewportView.SceneUpdating += OnRender;
        }

        private bool SeedFromContainers()
        {
            if (_compute is null) return false;

            // Get all containers
            List<FluidContainer>? containers = _domainGroups.SelectMany(g => g.Domains).ToList();
            List<Particle> seed = new();

            int offset = 0;
            uint domainId = 0;

            for (int gi = 0; gi < _domainGroups.Count; gi++)
                foreach (FluidContainer c in _domainGroups[gi].Domains)
                {
                    SceneNode node = c.ContainerNode;
                    node.UpdateWorldBoundingBox(true);
                    if (node.WorldBounds is null) return false;

                    BoundingBox bb = node.WorldBounds.BoundingBox;
                    c.BoxMin = bb.Minimum; c.BoxMax = bb.Maximum;

                    float s = c.Fluid.ParticleSpacing;
                    float fillTop = c.BoxMin.Y + (c.BoxMax.Y - c.BoxMin.Y) * c.FillFraction;

                    int before = seed.Count;
                    c.BufferOffset = offset;

                    for (float x = c.BoxMin.X + s; x < c.BoxMax.X - s; x += s)
                        for (float y = c.BoxMin.Y + s; y < fillTop; y += s)
                            for (float z = c.BoxMin.Z + s; z < c.BoxMax.Z - s; z += s)
                                seed.Add(new Particle { Position = new Vector3(x, y, z), DomainId = domainId, GroupId = (uint)gi });

                    c.ParticleCount = seed.Count - before;
                    offset += c.ParticleCount;
                    domainId++;
                }

            if (seed.Count == 0) return false;

            // Initialize GPU compute with seed
            _seedParticles = seed.ToArray();
            _compute.Seed(_seedParticles);

            // Initialize GPU grid
            FluidProperties fluid = _domainGroups[0].Domains[0].Fluid;
            _compute.InitializeGridHash(fluid.SupportRadius, fluid.ParticleMass, fluid.RestDensity, _compute.ParticleCount);
            _particleDiameter = fluid.ParticleSpacing;

            DomainBound[] bounds = containers.Select(c => new DomainBound
            {
                Min = new Vector4(c.BoxMin, 0f),
                Max = new Vector4(c.BoxMax, 0f),
            }).ToArray();
            _compute.InitializeSolver(bounds);

            // Record initial state & initialize visualization
            _cpuParticles = new Particle[_compute.ParticleCount];
            _renderPositions = _seedParticles.Select(p => p.Position).ToArray();
            _renderColours = new Color4[_renderPositions.Length];

            _pixels = new PixelsVisual3D(_renderPositions) 
            {
                PixelColor = new Color { ScR = 0.3f, ScG = 0.55f, ScB = 1f, ScA = 1f }, 
                PixelSize = 4f,
                PixelColors = _renderColours
            };
            _viewportView.Viewport3D.Children.Add(_pixels);

            return true;
        }

        // Render delegate
        private void OnRender(object? sender, EventArgs e)
        {
            if (!IsRunning || _compute is null) return;
            if (!_seeded) 
            { 
                if (!SeedFromContainers()) return; 

                _seeded = true;
                _renderTimer.Restart();
                return;
            }

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
            float vmax = 0f;

            for (int i = 0; i < _renderPositions.Length; i++)
            {
                _renderPositions[i] = _cpuParticles[i].Position;
                vmax = MathF.Max(vmax, _cpuParticles[i].Velocity.Length());

                float t = Math.Clamp(_cpuParticles[i].Density / 1000f, 0f, 1f);
                _renderColours[i] = new Color4(0.2f + 0.8f * t, 0.4f + 0.6f * t, 1f, 1f);
            }
            _vMax = vmax;

            _pixels.UpdatePositions();
            _pixels.UpdatePixelColors();
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

            // Compute particle substeps and stability criterion
            float cfl = _vMax > 1e-4f ? CFLFACTOR * _particleDiameter / _vMax : MaxTimeStep;
            float subDt = MathF.Min(MaxTimeStep, cfl);
            int subs = Math.Clamp((int)MathF.Ceiling(dt / subDt), 1, MaxSubsteps);
            _compute.UpdateSolverConstants(Gravity, subDt, Restitution);

            for (int s = 0; s < subs; s++)
            {
                _compute.BuildHash();
                _compute.ComputeDensity();
                _compute.ComputeFactor();

                for (int it = 0; it < DivergenceIterations; it++) { _compute.DivergenceAdv(); _compute.DivergenceCorrect(); }

                _compute.ApplyForces();

                for (int it = 0; it < PressureIterations; it++) { _compute.DensityAdv(); _compute.PressureCorrect(); }

                _compute.IntegratePos();
            }

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
