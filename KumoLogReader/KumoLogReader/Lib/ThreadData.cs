using System.Collections.Concurrent;

namespace KumoLogReader;

internal record ThreadData(string[] Lines, Mutex Mutex, SemaphoreSlim SemaphoreSlim, ConcurrentDictionary<string, Data> Dictionary, LockValue<int> ReadData, long FileSize);