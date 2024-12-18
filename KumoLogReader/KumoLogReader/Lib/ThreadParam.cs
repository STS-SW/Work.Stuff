using System.Collections.Concurrent;

namespace KumoLogReader.Lib;

internal record ThreadParam(string[] Lines, long TotalBytes, ConcurrentDictionary<string, LogData> Dictionary, Mutex ThreadMutex, SemaphoreSlim SemaphoreSlim, LockValue<int> BytesRead, Mutex ConsoleLogMutex);