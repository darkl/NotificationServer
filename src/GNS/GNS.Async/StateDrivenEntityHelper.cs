namespace GNS.Async;

/// <summary>
/// Helper class that encapsulates state-driven entity behavior using composition.
/// This allows the async state management to be reused across different entity types.
/// </summary>
public class StateDrivenEntityHelper : StateDrivenEntity, IDisposable
{
    private readonly Func<CancellationToken, Task> _innerUninitializedToInitialized;
    private readonly Func<CancellationToken, Task> _innerInitializedToStarted;
    private readonly Func<CancellationToken, Task> _innerStartedToInitialized;
    private readonly Func<CancellationToken, Task> _innerInitializedToUninitialized;
    private readonly Func<CancellationToken, Task> _innerAnyToInvalid;
    private readonly Func<CancellationToken, Task> _innerInvalidToUninitialized;

    public StateDrivenEntityHelper(
        Func<CancellationToken, Task> innerUninitializedToInitialized,
        Func<CancellationToken, Task> innerInitializedToStarted,
        Func<CancellationToken, Task> innerStartedToInitialized,
        Func<CancellationToken, Task> innerInitializedToUninitialized,
        Func<CancellationToken, Task> innerAnyToInvalid,
        Func<CancellationToken, Task> innerInvalidToUninitialized)
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

    protected override Task InnerUninitializedToInitializedAsync(CancellationToken cancellationToken = default)
    {
        return _innerUninitializedToInitialized(cancellationToken);
    }

    protected override Task InnerInitializedToStartedAsync(CancellationToken cancellationToken = default)
    {
        return _innerInitializedToStarted(cancellationToken);
    }

    protected override Task InnerStartedToInitializedAsync(CancellationToken cancellationToken = default)
    {
        return _innerStartedToInitialized(cancellationToken);
    }

    protected override Task InnerInitializedToUninitializedAsync(CancellationToken cancellationToken = default)
    {
        return _innerInitializedToUninitialized(cancellationToken);
    }

    protected override Task InnerAnyToInvalidAsync(CancellationToken cancellationToken = default)
    {
        return _innerAnyToInvalid(cancellationToken);
    }

    protected override Task InnerInvalidToUninitializedAsync(CancellationToken cancellationToken = default)
    {
        return _innerInvalidToUninitialized(cancellationToken);
    }
}