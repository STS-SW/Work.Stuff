using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace KumoLogReader;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var maxRows = 10000;
        var maxThreads = 100;
        var path = "E:\\Sync-TCPOS\\Projects\\Denner - 2023.11.19\\Performaces\\202412162.log";

        var r = new Regex("(?<guid>[a-z0-9]{8}(-[a-z0-9]{4}){3}-[a-z0-9]{12})");
        var aaaa = File.ReadLines(path).Select(l => r.Match(l)).Where(m => m.Success).Select(m => m.Value).Distinct().ToArray();
        Debug.WriteLine("Guids: " + aaaa.Length);

        var data = await ParseLog(path, maxThreads, maxRows);

        Debug.WriteLine("Guids: " + data.Length);

        foreach (var data1 in data)
        {
            Debug.WriteLine($"Request Id: {data1.Id}");
        }

        Debug.WriteLine("********** File parsed");
    }

    private static async Task<Data[]> ParseLog(string path, int maxThreads, int maxRows)
    {
        using var semaphoreSlim = new SemaphoreSlim(maxThreads, maxThreads);
        var lines = new List<string>();
        using var mutexes = new DisposableList<Mutex>();
        using var lockValue = new LockValue<int>(0);
        var l = new FileInfo(path).Length;
        var concurrentDictionary = new ConcurrentDictionary<string, Data>();

        await foreach (var line in File.ReadLinesAsync(path))
        {
            if (line == "}" && lines.Count > maxRows)
            {
                var thread = new Thread(ThreadAction);
                var mutex = new Mutex();
                mutexes.Add(mutex);

                thread.Start(new ThreadData(lines.ToArray(), mutex, semaphoreSlim, concurrentDictionary, lockValue, l));

                lines.Clear();
            }
            else
            {
                lines.Add(line);
            }
        }

        foreach (var waitHandle in mutexes.OfType<WaitHandle>().ToArray())
        {
            waitHandle.WaitOne();
        }

        return concurrentDictionary.Select(I => I.Value).ToArray();
    }

    private static void ThreadAction(object? o)
    {
        var threadData = o as ThreadData ?? throw new ArgumentNullException();

        var requestRegex = new Regex(@"(?i)^(?<time>\d{2}:\d{2}:\d{2}\.\d{3})\s*\[inf\]\s*request:\s*requestid:\s*(?<guid>[a-z0-9]{8}(-[a-z0-9]{4}){3}-[a-z0-9]{12})\s*method:\s*\[(?<method>\w+)\]\s*URI:\s*(?<url>.+?)\s*body:\s*({(?<body>.*?)}){0,1}$");

        var responseRegex = new Regex(@"(?i)^(?<time>\d{2}:\d{2}:\d{2}\.\d{3})\s*\[inf\]\s*response:\s*requestid:\s*(?<guid>[a-z0-9]{8}(-[a-z0-9]{4}){3}-[a-z0-9]{12})\s*statuscode:\s*(?<statusCode>\d{3})\s*response:\s*{{0,1}$");

        threadData.Mutex.WaitOne();
        threadData.SemaphoreSlim.Wait();

        try
        {
            var tmpData = default(Data);

            var init = 100.0 * threadData.ReadData.Value / threadData.FileSize;

            foreach (var line in threadData.Lines)
            {
                threadData.ReadData.Value += line.Length;
                var cur = 100.0 * threadData.ReadData.Value / threadData.FileSize;

                if (cur > init + 1)
                {
                    Debug.WriteLine($"{100.0 * threadData.ReadData.Value / threadData.FileSize:0.00}% ThreadId: {threadData.SemaphoreSlim.CurrentCount}");
                    init = cur;
                }

                if (tmpData != null)
                {
                    tmpData = tmpData with
                    {
                        ResponseData = tmpData.ResponseData with
                        {
                            ResponseBody = tmpData.ResponseData.ResponseBody + line
                        }
                    };

                    if (line == "}")
                    {
                        tmpData = UseResponse(threadData.Dictionary, tmpData);

                        //Debug.WriteLine(JsonSerializer.Serialize(tmpData, new JsonSerializerOptions
                        //{
                        //    WriteIndented = true
                        //}));

                        tmpData = null;
                    }

                    continue;
                }

                var match = requestRegex.Match(line);

                if (match.Success)
                {
                    var data = new Data(match.Groups["guid"].Value, new RequestData(TimeSpan.Parse(match.Groups["time"].Value, CultureInfo.InvariantCulture), match.Groups["url"].Value, match.Groups["method"].Value, "{" + match.Groups["body"].Value + "}"), new ResponseData(TimeSpan.MinValue, -1, ""));

                    UseRequest(threadData.Dictionary, data);

                    continue;
                }

                match = responseRegex.Match(line);

                if (match.Success)
                {
                    tmpData = new Data(match.Groups["guid"].Value, new RequestData(TimeSpan.MinValue, "", "", ""), new ResponseData(TimeSpan.Parse(match.Groups["time"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["statusCode"].Value), line.EndsWith("{") ? "{" : "{}"));

                    if (!line.EndsWith("{"))
                    {
                        tmpData = UseResponse(threadData.Dictionary, tmpData);

                        //Debug.WriteLine(JsonSerializer.Serialize(tmpData, new JsonSerializerOptions
                        //{
                        //    WriteIndented = true
                        //}));

                        tmpData = null;
                    }
                }
            }
        }
        finally
        {
            threadData.SemaphoreSlim.Release();
            threadData.Mutex.ReleaseMutex();
        }

        Debug.WriteLine("********** End thread");
    }

    private static Data UseResponse(ConcurrentDictionary<string, Data> threadDataDictionary, Data data)
    {
        var tmpData = data;

        if (threadDataDictionary.TryGetValue(tmpData.Id, out var value1))
        {
            tmpData = tmpData with
            {
                RequestData = value1.RequestData
            };
        }

        threadDataDictionary[tmpData.Id] = tmpData;
        return tmpData;
    }

    private static Data UseRequest(ConcurrentDictionary<string, Data> threadDataDictionary, Data data)
    {
        var tmpData = data;

        if (threadDataDictionary.TryGetValue(tmpData.Id, out var value1))
        {
            tmpData = tmpData with
            {
                ResponseData = value1.ResponseData
            };
        }

        threadDataDictionary[tmpData.Id] = tmpData;
        return tmpData;
    }
}
