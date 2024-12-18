namespace KumoLogReader.Lib;

internal static class LockExtensions
{
    private class LockObj : IDisposable
    {
        private readonly Action _releaseAction;

        public LockObj(Action waitAction, Action releaseAction)
        {
            _releaseAction = releaseAction;
            waitAction?.Invoke();
        }


        public static LockObj Wait(Action waitAction, Action releaseAction)
        {
            return new LockObj(waitAction, releaseAction);
        }

        public void Dispose()
        {
            _releaseAction?.Invoke();
        }
    }
    public static IDisposable Lock(this Mutex mutex)
    {
        return new LockObj(() => mutex.WaitOne(), mutex.ReleaseMutex);
    }
    public static IDisposable Lock(this SemaphoreSlim mutex)
    {
        return new LockObj(mutex.Wait, ()=>mutex.Release());
    }
}