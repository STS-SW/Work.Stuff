namespace KumoLogReader.Lib;

internal record RequestData(TimeSpan Time, string Url, string Method, string RequestBody);