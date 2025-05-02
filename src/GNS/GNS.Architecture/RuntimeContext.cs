namespace GNS.Architecture
{
    #region RuntimeContext Implementation
    public class RuntimeContext : IRuntimeContext
    {
        private readonly IComponentContainer _components;
        private readonly InputPort _inputPort;
        private readonly OutputPort _outputPort;

        public RuntimeContext()
        {
            _components = new ComponentContainer();
            _inputPort = new InputPort();
            _outputPort = new OutputPort();
        }

        public string ServerName { get; set; }
        public IComponentContainer Components => _components;
        public IServer Server { get; set; }
        public IInputPort InputPort => _inputPort;
        public IOutputPort OutputPort => _outputPort;
    }
    #endregion
}