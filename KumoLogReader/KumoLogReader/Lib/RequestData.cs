namespace KumoLogReader;

internal record RequestData(TimeSpan StartTime, string Url, string Method, string RequestBody);