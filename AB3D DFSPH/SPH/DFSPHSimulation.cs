using System.Numerics;
using System.Runtime.InteropServices;
using Ab3d.DirectX.Controls;
using SharpDX.Direct3D11;
using SPH.Simulation;
using SPH.Compute;


namespace SPH
{

    public sealed class DFSPHSimulation : IDisposable
    {

        private DXViewportView _viewportView;

        private Device? _device;
        private DeviceContext? _context;

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

            // Run hardware compute shader test
            Compute.Compute compute = new Compute.Compute(_device, _context);
            compute.RunSelfTest();
            compute.Dispose();
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
