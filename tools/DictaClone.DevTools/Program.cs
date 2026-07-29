using System.Text.Json;
using DictaClone.Audio;
using DictaClone.Speech;
using NAudio.Wave;

namespace DictaClone.DevTools;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            return args.FirstOrDefault()?.ToLowerInvariant() switch
            {
                "devices" => ListDevices(),
                "capture" => await CaptureAsync(args).ConfigureAwait(false),
                "benchmark" => await BenchmarkAsync(args).ConfigureAwait(false),
                _ => ShowUsage(),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int ListDevices()
    {
        IReadOnlyList<MicrophoneDeviceInfo> devices =
            WasapiMicrophoneProbe.GetActiveCaptureDevices();

        Console.WriteLine($"Active capture devices: {devices.Count}");

        foreach (MicrophoneDeviceInfo device in devices)
        {
            string marker = device.IsDefault ? "*" : " ";
            Console.WriteLine($"{marker} {device.FriendlyName}");
            Console.WriteLine($"  {device.Id}");
        }

        return devices.Count > 0 ? 0 : 2;
    }

    private static async Task<int> CaptureAsync(string[] args)
    {
        double seconds = args.Length >= 2
            ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture)
            : 1;
        CaptureProbeResult result = await WasapiMicrophoneProbe
            .CaptureAsync(TimeSpan.FromSeconds(seconds))
            .ConfigureAwait(false);

        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        return result.ReceivedAudioBuffers ? 0 : 3;
    }

    private static async Task<int> BenchmarkAsync(string[] args)
    {
        if (args.Length < 6 || (args.Length - 4) % 2 != 0)
        {
            return ShowUsage();
        }

        string wavePath = args[1];
        string expectedTranscriptPath = args[2];
        string outputPath = args[3];
        string expectedTranscript = await File
            .ReadAllTextAsync(expectedTranscriptPath)
            .ConfigureAwait(false);
        TimeSpan audioDuration;

        using (var waveReader = new WaveFileReader(wavePath))
        {
            audioDuration = waveReader.TotalTime;
        }

        int threadCount = Math.Clamp(Environment.ProcessorCount / 2, 1, 12);
        var results = new List<WhisperBenchmarkResult>();

        for (int index = 4; index < args.Length; index += 2)
        {
            string modelName = args[index];
            string modelPath = args[index + 1];
            Console.WriteLine($"Benchmarking {modelName} with {threadCount} threads...");

            WhisperBenchmarkResult result = await WhisperBenchmarkRunner
                .RunAsync(
                    modelName,
                    modelPath,
                    wavePath,
                    audioDuration,
                    expectedTranscript,
                    threadCount)
                .ConfigureAwait(false);
            results.Add(result);

            Console.WriteLine(
                $"{modelName}: load={result.ModelLoadDuration.TotalMilliseconds:F0} ms, " +
                $"inference={result.InferenceDuration.TotalMilliseconds:F0} ms, " +
                $"RTF={result.RealTimeFactor:F3}, " +
                $"WER={result.TranscriptScore.WordErrorRate:P1}, " +
                $"peak={result.PeakWorkingSetBytes / 1024d / 1024d:F1} MiB");
            Console.WriteLine($"Transcript: {result.Transcript}");
        }

        string? outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));

        if (outputDirectory is not null)
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await File.WriteAllTextAsync(
                outputPath,
                JsonSerializer.Serialize(results, JsonOptions))
            .ConfigureAwait(false);
        Console.WriteLine($"Wrote {Path.GetFullPath(outputPath)}");
        return 0;
    }

    private static int ShowUsage()
    {
        Console.WriteLine(
            """
            DictaClone Milestone 0 developer tools

              devices
              capture [seconds]
              benchmark <wave> <expected.txt> <output.json> <model-name> <model-path> [<model-name> <model-path>...]
            """);
        return 64;
    }
}
