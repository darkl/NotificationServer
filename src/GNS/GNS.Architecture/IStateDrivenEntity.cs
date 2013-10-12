using System;

namespace GNS.Architecture
{
    public enum State
    {
        Uninitialized,
        Initialized,
        Started,
        Invalid
    }

    public interface IStateDrivenEntity
    {
        State CurrentState { get; }
        void TransformTo(State state);

        event EventHandler<StateTransformEventArgs> StateTransforming;
        event EventHandler<StateTransformEventArgs> StateTransformed;
    }

    public class StateTransformEventArgs : EventArgs
    {
        public State PreviousState { get; set; }
        public State NextState { get; set; }
    }

    public abstract class StateDrivenEntity : IStateDrivenEntity
    {
        public State CurrentState
        {
            get
            {
                throw new System.NotImplementedException();
            }
        }

        public virtual void TransformTo(State state)
        {
            throw new System.NotImplementedException();
        }

        public event EventHandler<StateTransformEventArgs> StateTransforming;
        public event EventHandler<StateTransformEventArgs> StateTransformed;

        protected abstract void InnerUninitializedToInitialized();
        protected abstract void InnerInitializedToUninitialized();
        protected abstract void InnerInitializedToStarted();
        protected abstract void InnerStartedToInitialized();
        protected abstract void InnerAnyToInvalid();
        protected abstract void InnerInvalidToUnitialized();
    }
}