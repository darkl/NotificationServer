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
        private readonly object _stateLock = new object();

        public State CurrentState { get; private set; } = State.Uninitialized;

        public event EventHandler<StateTransformEventArgs> StateTransforming;
        public event EventHandler<StateTransformEventArgs> StateTransformed;

        protected abstract void InnerUninitializedToInitialized();
        protected abstract void InnerInitializedToUninitialized();
        protected abstract void InnerInitializedToStarted();
        protected abstract void InnerStartedToInitialized();
        protected abstract void InnerAnyToInvalid();
        protected abstract void InnerInvalidToUninitialized();

        public virtual void TransformTo(State state)
        {
            lock (_stateLock)
            {
                try
                {
                    if (state == CurrentState)
                        return;

                    switch (CurrentState, state)
                    {
                        case (not State.Invalid, not State.Invalid):
                            ValidStateTransform(state);
                            break;
                        case (not State.Invalid, State.Invalid):
                            var invalidArgs = new StateTransformEventArgs
                            {
                                PreviousState = CurrentState,
                                NextState = State.Invalid
                            };
                            OnStateTransforming(invalidArgs);
                            InnerAnyToInvalid();
                            CurrentState = State.Invalid;
                            OnStateTransformed(invalidArgs);
                            break;
                        case (State.Invalid, not State.Invalid):
                            var uninitArgs = new StateTransformEventArgs
                            {
                                PreviousState = State.Invalid,
                                NextState = State.Uninitialized
                            };
                            OnStateTransforming(uninitArgs);
                            InnerInvalidToUninitialized();
                            CurrentState = State.Uninitialized;
                            OnStateTransformed(uninitArgs);

                            // Continue with the requested state transition
                            if (state != State.Uninitialized)
                                ValidStateTransform(state);
                            break;
                    }
                }
                catch (Exception e)
                {
                    // On exception, transition to Invalid state if not already there
                    if (CurrentState != State.Invalid)
                        TransformTo(State.Invalid);
                }
            }
        }

        private void ValidStateTransform(State state)
        {
            switch (CurrentState, state)
            {
                case (State.Started, State.Uninitialized):
                    // Go through Initialized when transitioning from Started to Uninitialized
                    SingleStateTransform(State.Initialized);
                    SingleStateTransform(State.Uninitialized);
                    break;
                case (State.Uninitialized, State.Started):
                    // Go through Initialized when transitioning from Uninitialized to Started
                    SingleStateTransform(State.Initialized);
                    SingleStateTransform(State.Started);
                    break;
                default:
                    SingleStateTransform(state);
                    break;
            }
        }

        private void SingleStateTransform(State state)
        {
            var args = new StateTransformEventArgs
            {
                PreviousState = CurrentState,
                NextState = state
            };

            OnStateTransforming(args);

            switch (CurrentState, state)
            {
                case (State.Uninitialized, State.Initialized):
                    InnerUninitializedToInitialized();
                    CurrentState = State.Initialized;
                    break;
                case (State.Initialized, State.Started):
                    InnerInitializedToStarted();
                    CurrentState = State.Started;
                    break;
                case (State.Started, State.Initialized):
                    InnerStartedToInitialized();
                    CurrentState = State.Initialized;
                    break;
                case (State.Initialized, State.Uninitialized):
                    InnerInitializedToUninitialized();
                    CurrentState = State.Uninitialized;
                    break;
            }

            OnStateTransformed(args);
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

    #region Helper Implementation
    public class StateDrivenEntityHelper : StateDrivenEntity
    {
        private readonly Action _innerUninitializedToInitialized;
        private readonly Action _innerInitializedToStarted;
        private readonly Action _innerStartedToInitialized;
        private readonly Action _innerInitializedToUninitialized;
        private readonly Action _innerAnyToInvalid;
        private readonly Action _innerInvalidToUninitialized;

        public StateDrivenEntityHelper(
            Action innerUninitializedToInitialized,
            Action innerInitializedToStarted,
            Action innerStartedToInitialized,
            Action innerInitializedToUninitialized,
            Action innerAnyToInvalid,
            Action innerInvalidToUninitialized)
        {
            _innerUninitializedToInitialized = innerUninitializedToInitialized ??
                throw new ArgumentNullException(nameof(innerUninitializedToInitialized));

            _innerInitializedToStarted = innerInitializedToStarted ??
                throw new ArgumentNullException(nameof(innerInitializedToStarted));

            _innerStartedToInitialized = innerStartedToInitialized ??
                throw new ArgumentNullException(nameof(innerStartedToInitialized));

            _innerInitializedToUninitialized = innerInitializedToUninitialized ??
                throw new ArgumentNullException(nameof(innerInitializedToUninitialized));

            _innerAnyToInvalid = innerAnyToInvalid ??
                throw new ArgumentNullException(nameof(innerAnyToInvalid));

            _innerInvalidToUninitialized = innerInvalidToUninitialized ??
                throw new ArgumentNullException(nameof(innerInvalidToUninitialized));
        }

        protected override void InnerUninitializedToInitialized()
        {
            _innerUninitializedToInitialized();
        }

        protected override void InnerInitializedToStarted()
        {
            _innerInitializedToStarted();
        }

        protected override void InnerStartedToInitialized()
        {
            _innerStartedToInitialized();
        }

        protected override void InnerInitializedToUninitialized()
        {
            _innerInitializedToUninitialized();
        }

        protected override void InnerAnyToInvalid()
        {
            _innerAnyToInvalid();
        }

        protected override void InnerInvalidToUninitialized()
        {
            _innerInvalidToUninitialized();
        }
    }

    #endregion
}