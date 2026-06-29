

namespace SPH.Simulation
{

    public sealed class DomainGroup
    {

        public DomainGroup(List<FluidContainer> domains)
        {
            _domains = domains;
        }

        public DomainGroup(FluidContainer domain)
        {
            _domains = new List<FluidContainer>() { domain };
        }

        public DomainGroup()
        {
            _domains = new List<FluidContainer>();
        }

        #region Properties

        private readonly List<FluidContainer> _domains;
        public IReadOnlyList<FluidContainer> Domains
        { 
            get => _domains; 
        }

        public bool IsRunning { get; set; } = true;
        public string? Name { get; set; }

        #endregion

        public void RemoveDomain(FluidContainer domain)
        {
            if (_domains.Contains(domain)) _domains.Remove(domain);
        }

        public void AddDomain(FluidContainer domain)
        {
            if (!_domains.Contains(domain)) _domains.Add(domain);
        }

    }

}
