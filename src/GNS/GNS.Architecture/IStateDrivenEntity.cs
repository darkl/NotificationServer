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

    #region StateDrivenEntity Implementation
    public abstract partial class StateDrivenEntity : IStateDrivenEntity
    {
        private State _currentState = State.Uninitialized;
        private readonly object _stateLock = new object();

        public State CurrentState
        {
            get { return _currentState; }
            private set { _currentState = value; }
        }

        public event EventHandler<StateTransformEventArgs> StateTransforming;
        public event EventHandler<StateTransformEventArgs> StateTransformed;

        protected abstract void InnerUninitializedToInitialized();
        protected abstract void InnerInitializedToUninitialized();
        protected abstract void InnerInitializedToStarted();
        protected abstract void InnerStartedToInitialized();
        protected abstract void InnerAnyToInvalid();
        protected abstract void InnerInvalidToUnitialized();

        public virtual void TransformTo(State state)
        {
            lock (_stateLock)
            {
                if (state == CurrentState)
                    return;

                var args = new StateTransformEventArgs
                {
                    PreviousState = CurrentState,
                    NextState = state
                };

                OnStateTransforming(args);

                switch (CurrentState)
                {
                    case State.Uninitialized:
                        if (state == State.Initialized)
                            InnerUninitializedToInitialized();
                        break;

                    case State.Initialized:
                        if (state == State.Uninitialized)
                            InnerInitializedToUninitialized();
                        else if (state == State.Started)
                            InnerInitializedToStarted();
                        break;

                    case State.Started:
                        if (state == State.Initialized)
                            InnerStartedToInitialized();
                        break;
                }

                if (state == State.Invalid)
                    InnerAnyToInvalid();
                else if (CurrentState == State.Invalid && state == State.Uninitialized)
                    InnerInvalidToUnitialized();

                CurrentState = state;
                OnStateTransformed(args);
            }
        }

        protected virtual void OnStateTransforming(StateTransformEventArgs e)
        {
            StateTransforming?.Invoke(this, e);
        }

        protected virtual void OnStateTransformed(StateTransformEventArgs e)
        {
            StateTransformed?.Invoke(this, e);
        }
    }
    #endregion

}