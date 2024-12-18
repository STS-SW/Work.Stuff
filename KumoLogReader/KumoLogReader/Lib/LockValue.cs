namespace KumoLogReader.Lib;

internal class LockValue<T>(T value) : IDisposable
{
    private readonly bool _isClass = typeof(T).IsClass;
    private readonly Mutex _mutex = new();
    private T _value = value;

    public T Value
    {
        get
        {
            if (!_isClass)
            {
                return _value;
            }

            using var _ = _mutex.Lock();
            return _value;
        }
        set
        {
            if (_isClass)
            {
                using var _ = _mutex.Lock();
                _value = value;
            }
            else
            {
                _value = value;
            }
        }
    }

    public void Dispose()
    {
        _mutex.Dispose();
    }
}
