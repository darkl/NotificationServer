namespace GNS.Async;

public abstract class StateDrivenEntity : IStateDrivenEntity
{
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    public State CurrentState { get; private set; } = State.Uninitialized;

    public event EventHandler<StateTransformEventArgs> StateTransforming;
    public event EventHandler<StateTransformEventArgs> StateTransformed;

    protected abstract Task InnerUninitializedToInitializedAsync(CancellationToken cancellationToken = default);
    protected abstract Task InnerInitializedToUninitializedAsync(CancellationToken cancellationToken = default);
    protected abstract Task InnerInitializedToStartedAsync(CancellationToken cancellationToken = default);
    protected abstract Task InnerStartedToInitializedAsync(CancellationToken cancellationToken = default);
    protected abstract Task InnerAnyToInvalidAsync(CancellationToken cancellationToken = default);
    protected abstract Task InnerInvalidToUninitializedAsync(CancellationToken cancellationToken = default);

    public virtual async Task TransformToAsync(State state, CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (state == CurrentState)
                return;

            switch (CurrentState, state)
            {
                case (not State.Invalid, not State.Invalid):
                    await ValidStateTransformAsync(state, cancellationToken);
                    break;
                case (not State.Invalid, State.Invalid):
                    var invalidArgs = new StateTransformEventArgs
                    {
                        PreviousState = CurrentState,
                        NextState = State.Invalid
                    };
                    OnStateTransforming(invalidArgs);
                    await InnerAnyToInvalidAsync(cancellationToken);
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
                    await InnerInvalidToUninitializedAsync(cancellationToken);
                    CurrentState = State.Uninitialized;
                    OnStateTransformed(uninitArgs);

                    // Continue with the requested state transition
                    if (state != State.Uninitialized)
                        await ValidStateTransformAsync(state, cancellationToken);
                    break;
            }
        }
        catch (Exception e)
        {
            // On exception, transition to Invalid state if not already there
            if (CurrentState != State.Invalid)
                await TransformToAsync(State.Invalid, cancellationToken);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task ValidStateTransformAsync(State state, CancellationToken cancellationToken)
    {
        switch (CurrentState, state)
        {
            case (State.Started, State.Uninitialized):
                // Go through Initialized when transitioning from Started to Uninitialized
                await SingleStateTransformAsync(State.Initialized, cancellationToken);
                await SingleStateTransformAsync(State.Uninitialized, cancellationToken);
                break;
            case (State.Uninitialized, State.Started):
                // Go through Initialized when transitioning from Uninitialized to Started
                await SingleStateTransformAsync(State.Initialized, cancellationToken);
                await SingleStateTransformAsync(State.Started, cancellationToken);
                break;
            default:
                await SingleStateTransformAsync(state, cancellationToken);
                break;
        }
    }

    private async Task SingleStateTransformAsync(State state, CancellationToken cancellationToken)
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
                await InnerUninitializedToInitializedAsync(cancellationToken);
                CurrentState = State.Initialized;
                break;
            case (State.Initialized, State.Started):
                await InnerInitializedToStartedAsync(cancellationToken);
                CurrentState = State.Started;
                break;
            case (State.Started, State.Initialized):
                await InnerStartedToInitializedAsync(cancellationToken);
                CurrentState = State.Initialized;
                break;
            case (State.Initialized, State.Uninitialized):
                await InnerInitializedToUninitializedAsync(cancellationToken);
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

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stateLock?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}