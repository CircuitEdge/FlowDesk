namespace FlowDeck.Services;

internal sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\FlowDeck.SingleInstance";
    private const string ActivationEventName = @"Local\FlowDeck.Activate";

    private readonly Mutex _mutex = new(false, MutexName);
    private readonly EventWaitHandle _activationEvent =
        new(false, EventResetMode.AutoReset, ActivationEventName);
    private readonly EventWaitHandle _stopEvent = new(false, EventResetMode.ManualReset);
    private Task? _listener;
    private bool _ownsMutex;

    public bool TryAcquirePrimaryInstance(TimeSpan? waitTimeout = null)
    {
        try
        {
            _ownsMutex = _mutex.WaitOne(waitTimeout ?? TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }

        return _ownsMutex;
    }

    public void NotifyPrimaryInstance() => _activationEvent.Set();

    public void StartListening(Action activate)
    {
        if (!_ownsMutex)
            throw new InvalidOperationException("The primary instance must own the single-instance mutex.");

        _listener = Task.Run(() =>
        {
            WaitHandle[] handles = [_stopEvent, _activationEvent];
            while (WaitHandle.WaitAny(handles) == 1)
                activate();
        });
    }

    public void Dispose()
    {
        _stopEvent.Set();
        _listener?.GetAwaiter().GetResult();

        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _stopEvent.Dispose();
        _activationEvent.Dispose();
        _mutex.Dispose();
    }
}
