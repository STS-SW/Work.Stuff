namespace KumoLogReader;

internal class DisposableList<T> : List<T>, IDisposable
    where T : IDisposable
{
    public void Dispose()
    {
        foreach (var o in this)
        {
            o?.Dispose();
        }
    }
}