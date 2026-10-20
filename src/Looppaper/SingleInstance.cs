namespace Looppaper;

/// <summary>
/// One Looppaper per user session. The running instance owns the mutex and listens on a named
/// event; <c>--stop</c> sets the event and waits for the mutex to be released.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Local\Looppaper";
    public const string StopEventName = @"Local\Looppaper.Stop";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _stopEvent;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle stopEvent)
    {
        _mutex = mutex;
        _stopEvent = stopEvent;
    }

    /// <summary>Claims the instance, or returns null when another Looppaper is already running.</summary>
    public static SingleInstance? TryClaim()
    {
        var mutex = new Mutex(false, MutexName);
        try
        {
            if (!mutex.WaitOne(0))
            {
                mutex.Dispose();
                return null;
            }
        }
        catch (AbandonedMutexException)
        {
            // A previous instance crashed; the mutex is ours now.
        }

        var stopEvent = new EventWaitHandle(false, EventResetMode.AutoReset, StopEventName);
        stopEvent.Reset(); // ignore a stale --stop that arrived while nothing was running
        return new SingleInstance(mutex, stopEvent);
    }

    /// <summary>Invokes <paramref name="onStop"/> on a thread-pool thread when --stop is signalled.</summary>
    public void ListenForStop(Action onStop)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(_stopEvent, (_, _) => onStop(), null, Timeout.Infinite, executeOnlyOnce: true);
    }

    public enum StopResult
    {
        Stopped,
        NotRunning,
        TimedOut,
    }

    /// <summary>Asks the running instance to stop and waits until it has exited.</summary>
    public static StopResult SignalStop(TimeSpan timeout)
    {
        if (!EventWaitHandle.TryOpenExisting(StopEventName, out var stopEvent))
        {
            return StopResult.NotRunning;
        }

        using (stopEvent)
        {
            stopEvent.Set();
        }

        if (!Mutex.TryOpenExisting(MutexName, out var mutex))
        {
            return StopResult.Stopped;
        }

        using (mutex)
        {
            try
            {
                if (!mutex.WaitOne(timeout))
                {
                    return StopResult.TimedOut;
                }
            }
            catch (AbandonedMutexException)
            {
                // Released by process exit without ReleaseMutex; still means it is gone.
            }

            mutex.ReleaseMutex();
            return StopResult.Stopped;
        }
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _stopEvent.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread; disposing still releases the handle.
        }

        _mutex.Dispose();
    }
}
