namespace KumoLogReader.Lib;

internal record ResponseData(TimeSpan Time, int StatusCode, string ResponseBody);