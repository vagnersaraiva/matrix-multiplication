using System.Text.Json;
using MatrixMultiply.Core.Api;
using MatrixMultiply.Core.Hash;
using MatrixMultiply.Core.Orchestration;

if (args.Contains("--help") || args.Contains("-h"))
{
    CliOptions.PrintHelp();
    return 0;
}

CliOptions options;
try
{
    options = CliOptions.Parse(args, LoadSettings());
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
using var http = MatrixApiClient.CreateHttpClient(options.BaseUrl);
var api = new MatrixApiClient(http, options.InitialConcurrency, options.MaxConcurrency);

if (options.Calibrate)
{
    var calibration = new CalibrationService(api);
    var layout = await calibration.FindLayoutAsync(options.CalibrateN, Console.WriteLine, CancellationToken.None).ConfigureAwait(false);
    if (layout is null)
    {
        Console.Error.WriteLine("No candidate hash layout was accepted.");
        return 2;
    }

    Console.WriteLine($"Winning layout: {layout}");
    return 0;
}

var service = new MatrixRunService(api, options.Layout);
var report = await service.ExecuteAsync(options.N, CancellationToken.None).ConfigureAwait(false);
var text = TimingReport.Format(report);
Console.Write(text);

var artifacts = Path.Combine(Directory.GetCurrentDirectory(), "artifacts");
Directory.CreateDirectory(artifacts);
await File.WriteAllTextAsync(Path.Combine(artifacts, "timings.txt"), text).ConfigureAwait(false);

if (!report.Succeeded)
{
    Console.Error.WriteLine($"Hash was not accepted: {report.Hash}");
    return 1;
}

return 0;

static AppSettings LoadSettings()
{
    var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    if (!File.Exists(path))
    {
        return new AppSettings();
    }

    var json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    }) ?? new AppSettings();
}

internal sealed class AppSettings
{
    public string BaseUrl { get; set; } = "https://takehome-api-231097775515.us-central1.run.app";

    public int N { get; set; } = 1000;

    public int InitialConcurrency { get; set; } = 64;

    public int MaxConcurrency { get; set; } = 128;

    public int CalibrateN { get; set; } = 3;
}

internal sealed record CliOptions(
    string BaseUrl,
    int N,
    int InitialConcurrency,
    int MaxConcurrency,
    int CalibrateN,
    HashLayout Layout,
    bool Calibrate)
{
    public static CliOptions Parse(string[] args, AppSettings settings)
    {
        var baseUrl = settings.BaseUrl;
        var n = settings.N;
        var initial = settings.InitialConcurrency;
        var max = settings.MaxConcurrency;
        var calibrateN = settings.CalibrateN;
        var layout = ResultHasher.DefaultLayout;
        var calibrate = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--calibrate":
                    calibrate = true;
                    break;
                case "--n":
                    n = ReadInt(args, ref i, arg);
                    break;
                case "--calibrate-n":
                    calibrateN = ReadInt(args, ref i, arg);
                    break;
                case "--initial-concurrency":
                    initial = ReadInt(args, ref i, arg);
                    break;
                case "--max-concurrency":
                    max = ReadInt(args, ref i, arg);
                    break;
                case "--base-url":
                    baseUrl = ReadString(args, ref i, arg);
                    break;
                case "--layout":
                    var raw = ReadString(args, ref i, arg);
                    if (!Enum.TryParse(raw, ignoreCase: true, out layout))
                    {
                        throw new ArgumentException($"Unknown hash layout '{raw}'.");
                    }

                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{arg}'.");
            }
        }

        if (n is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "n must be from 1 to 10000.");
        }

        if (calibrateN is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(calibrateN), "calibrate-n must be from 1 to 10000.");
        }

        if (initial < 1 || max < initial)
        {
            throw new ArgumentOutOfRangeException(nameof(initial), "Concurrency must satisfy 1 <= initial <= max.");
        }

        return new CliOptions(baseUrl, n, initial, max, calibrateN, layout, calibrate);
    }

    private static int ReadInt(string[] args, ref int index, string name)
    {
        var value = ReadString(args, ref index, name);
        if (!int.TryParse(value, out var parsed))
        {
            throw new ArgumentException($"Expected an integer after {name}.");
        }

        return parsed;
    }

    private static string ReadString(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Expected a value after {name}.");
        }

        index++;
        return args[index];
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            Matrix multiplication client

              docker run --rm -v "$PWD/artifacts:/app/artifacts" matrix-multiply --n 1000
              docker run --rm matrix-multiply --calibrate

            Options:
              --n <int>                     Matrix size (1..10000). Default 1000.
              --calibrate                   Probe MD5 layouts against a small init.
              --calibrate-n <int>           Size used by --calibrate. Default 3.
              --initial-concurrency <int>   Starting parallel GETs. Default 64.
              --max-concurrency <int>       AIMD ceiling. Default 128.
              --base-url <url>              API origin. No port.
              --layout <name>               Force a hash layout.
            """);
    }
}
