using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using KumoLogReader.Lib;
using OfficeOpenXml;

namespace KumoLogReader;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var maxRows = 10000;
        var maxThreads = 100;
        var path = "E:\\Sync-TCPOS\\Projects\\Denner - 2023.11.19\\Performaces\\202412162.log";

        Console.WriteLine($"Start reading log: {path}");
        Console.WriteLine("Determining guids number");
        var guidRegex = new Regex("(?<guid>[a-z0-9]{8}(-[a-z0-9]{4}){3}-[a-z0-9]{12})");
        var guids = File.ReadLines(path).Select(l => guidRegex.Match(l)).Where(m => m.Success).Select(m => m.Value).Distinct().ToArray();
        Console.WriteLine($"Guids found: {guids.Length}");

        Console.WriteLine("Parsing log");
        Console.WriteLine("");
        var data = await ParseLog(path, maxThreads, maxRows);

        var fileName = $"{path}.xlsx";
        Console.WriteLine($"Writing excel file: {fileName}");

        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var excelPackage = new ExcelPackage(fileName);

        foreach (var workbookWorksheet in excelPackage.Workbook.Worksheets.ToArray())
        {
            excelPackage.Workbook.Worksheets.Delete(workbookWorksheet.Name);
        }
        var worksheet = excelPackage.Workbook.Worksheets.Add("Raw-Data");
        worksheet.Cells.LoadFromCollection(data.Where(i => i.RequestData.Time > TimeSpan.MinValue && i.ResponseData.Time > TimeSpan.MinValue).Select(i => new
        {
            Guid = i.Id,
            StartTime = i.RequestData.Time,
            EndTime = i.ResponseData.Time,
            (i.ResponseData.Time - i.RequestData.Time).TotalSeconds,
            i.RequestData.Method,
            i.RequestData.Url,
            //i.RequestData.RequestBody,
            i.ResponseData.StatusCode,
            //i.ResponseData.ResponseBody
        }),true);
        await excelPackage.SaveAsync();
        Console.WriteLine("Done");
    }

    private static async Task<LogData[]> ParseLog(string path, int maxThreads, int maxRows)
    {
        using var consoleLogMutex = new Mutex();
        using var semaphoreSlim = new SemaphoreSlim(maxThreads, maxThreads);
        using var mutexes = new DisposableList<Mutex>();
        using var lockValue = new LockValue<int>(0);

        var lines = new List<string>();
        var fileSize = new FileInfo(path).Length;
        var concurrentDictionary = new ConcurrentDictionary<string, LogData>();

        await foreach (var line in File.ReadLinesAsync(path))
        {
            if (line == "}" && lines.Count > maxRows)
            {
                var thread = new Thread(ThreadAction);
                var mutex = new Mutex();
                mutexes.Add(mutex);

                thread.Start(new ThreadParam(lines.ToArray(), fileSize, concurrentDictionary, mutex, semaphoreSlim, lockValue, consoleLogMutex));

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
        var threadParams = o as ThreadParam ?? throw new ArgumentNullException();

        var requestRegex = new Regex(@"(?i)^(?<time>\d{2}:\d{2}:\d{2}\.\d{3})\s*\[inf\]\s*request:\s*requestid:\s*(?<guid>[a-z0-9]{8}(-[a-z0-9]{4}){3}-[a-z0-9]{12})\s*method:\s*\[(?<method>\w+)\]\s*URI:\s*(?<url>.+?)\s*body:\s*({(?<body>.*?)}){0,1}$");

        var responseRegex = new Regex(@"(?i)^(?<time>\d{2}:\d{2}:\d{2}\.\d{3})\s*\[inf\]\s*response:\s*requestid:\s*(?<guid>[a-z0-9]{8}(-[a-z0-9]{4}){3}-[a-z0-9]{12})\s*statuscode:\s*(?<statusCode>\d{3})\s*response:\s*{{0,1}$");

        using var ml = threadParams.ThreadMutex.Lock();
        using var sl = threadParams.SemaphoreSlim.Lock();

        var tmpData = default(LogData);

        var prevPercentage = 100.0 * threadParams.BytesRead.Value / threadParams.TotalBytes;

        foreach (var line in threadParams.Lines)
        {
            threadParams.BytesRead.Value += line.Length;

            var currentPercentage = 100.0 * threadParams.BytesRead.Value / threadParams.TotalBytes;

            if (currentPercentage > prevPercentage + 1)
            {
                using var _ = threadParams.ConsoleLogMutex.Lock();

                var cursorTop = Console.CursorTop;
                var cursorLeft = Console.CursorLeft;

                Console.Write($"[{ProgressBar(currentPercentage, 20)}] {currentPercentage:0.00}%");
                prevPercentage = currentPercentage;

                Console.CursorTop = cursorTop;
                Console.CursorLeft = cursorLeft;
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
                    tmpData = UseResponse(threadParams.Dictionary, tmpData);

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
                var data = new LogData(match.Groups["guid"].Value, new RequestData(TimeSpan.Parse(match.Groups["time"].Value, CultureInfo.InvariantCulture), match.Groups["url"].Value, match.Groups["method"].Value, "{" + match.Groups["body"].Value + "}"), new ResponseData(TimeSpan.MinValue, -1, ""));

                UseRequest(threadParams.Dictionary, data);

                continue;
            }

            match = responseRegex.Match(line);

            if (match.Success)
            {
                tmpData = new LogData(match.Groups["guid"].Value, new RequestData(TimeSpan.MinValue, "", "", ""), new ResponseData(TimeSpan.Parse(match.Groups["time"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["statusCode"].Value), line.EndsWith("{") ? "{" : "{}"));

                if (!line.EndsWith("{"))
                {
                    tmpData = UseResponse(threadParams.Dictionary, tmpData);

                    //Debug.WriteLine(JsonSerializer.Serialize(tmpData, new JsonSerializerOptions
                    //{
                    //    WriteIndented = true
                    //}));

                    tmpData = null;
                }
            }
        }

        Debug.WriteLine("********** End thread");
    }

    private static string ProgressBar(double currentPercentage, int barLength)
    {
        var completed = barLength * currentPercentage / 100;
        return string.Join("", Enumerable.Range(0, barLength).Select(i => i < completed ? "*" : " "));
    }

    private static LogData UseResponse(ConcurrentDictionary<string, LogData> threadDataDictionary, LogData logData)
    {
        var tmpData = logData;

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

    private static LogData UseRequest(ConcurrentDictionary<string, LogData> threadDataDictionary, LogData logData)
    {
        var tmpData = logData;

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
