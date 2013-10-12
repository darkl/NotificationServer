namespace GNS.Architecture
{
    internal class RuntimeContext : IRuntimeContext
    {
        private IComponentContainer mComponents;

        public string ServerName
        {
            get; 
            set;
        }

        public IComponentContainer Components
        {
            get
            {
                return mComponents;
            }
        }

        public IServer Server { get; set; }
        public IInputPort InputPort { get; set; }
        public IOutputPort OutputPort { get; set; }
    }
}