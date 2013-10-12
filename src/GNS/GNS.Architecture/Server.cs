using System;

namespace GNS.Architecture
{
    public class Server : IServer
    {
        private readonly RuntimeContext mRuntimeContext;

        public Server(string serverName, IComponentBuilder builder)
        {
            mRuntimeContext = new RuntimeContext();

            mRuntimeContext.ServerName = serverName;
            mRuntimeContext.Server = this;

            builder.BuildComponents(mRuntimeContext);
        }

        public State CurrentState
        {
            get { throw new System.NotImplementedException(); }
        }

        public void TransformTo(State state)
        {
            foreach (IComponent component in mRuntimeContext.Components)
            {
                // A bit inaccurate, we need to override all state methods,
                // so we can add some determinism. Also, the components should
                // be ordered by hierarchy
                component.TransformTo(state);
            }
        }

        public event EventHandler<StateTransformEventArgs> StateTransforming;
        public event EventHandler<StateTransformEventArgs> StateTransformed;

        public IRuntimeContext RuntimeContext
        {
            get
            {
                return mRuntimeContext;
            }
        }
    }
}