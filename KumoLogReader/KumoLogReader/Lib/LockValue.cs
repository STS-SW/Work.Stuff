namespace KumoLogReader;

internal class LockValue<T>(T value) : IDisposable
{
    private readonly bool _isClass = typeof(T).IsClass;
    private readonly Mutex _mutex = new();
    private T _value = value;

    public T Value
    {
        get
        {
            if (_isClass)
            {
                _mutex.WaitOne();
            }

            try
            {
                return _value;
            }
            finally
            {
                if (_isClass)
                {
                    _mutex.ReleaseMutex();
                }
            }
        }
        set
        {
            if (_isClass)
            {
                _mutex.WaitOne();
            }

            try
            {
                _value = value;
            }
            finally
            {
                if (_isClass)
                {
                    _mutex.ReleaseMutex();
                }
            }
        }
    }

    public void Dispose()
    {
        _mutex.Dispose();
    }
}
