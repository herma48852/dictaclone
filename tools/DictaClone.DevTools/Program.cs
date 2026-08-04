using System.Buffers.Binary;
using System.Text.Json;
using DictaClone.Audio;
using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;
using DictaClone.Core.Settings;
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
                "capture-pcm" => await CapturePcmAsync(args).ConfigureAwait(false),
                "capture-transcribe" => await CaptureTranscribeAsync(args)
                    .ConfigureAwait(false),
                "benchmark" => await BenchmarkAsync(args).ConfigureAwait(false),
                "transcribe" => await TranscribeAsync(args).ConfigureAwait(false),
                "speech-regression" => await SpeechRegressionAsync(args)
                    .ConfigureAwait(false),
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
              capture-pcm [seconds] [device-id]
              capture-transcribe [seconds] [model-name] [device-id]
              benchmark <wave> <expected.txt> <output.json> <model-name> <model-path> [<model-name> <model-path>...]
              transcribe <wave> <model-name> [model-directory]
              speech-regression <wave> <expected.txt> <model-name> <max-wer> [model-directory]
            """);
        return 64;
    }

    private static async Task<int> CapturePcmAsync(string[] args)
    {
        if (args.Length > 3)
        {
            return ShowUsage();
        }

        double seconds = args.Length >= 2
            ? double.Parse(
                args[1],
                System.Globalization.CultureInfo.InvariantCulture)
            : 1;
        if (seconds is <= 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args),
                "Capture duration must be greater than zero and no more than ten seconds.");
        }

        string? deviceId = args.Length == 3 ? args[2] : null;
        LiveCaptureProbe probe = await CaptureLivePcmAsync(seconds, deviceId)
            .ConfigureAwait(false);
        CapturedAudio audio = probe.Audio;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            audio.SampleRate,
            audio.ChannelCount,
            audio.Duration,
            PcmBytes = audio.Pcm16.Length,
            audio.IsSilent,
            probe.MaximumLevel,
        }, JsonOptions));
        return audio.Pcm16.IsEmpty ? 4 : 0;
    }

    private static async Task<int> CaptureTranscribeAsync(string[] args)
    {
        if (args.Length > 4)
        {
            return ShowUsage();
        }

        double seconds = args.Length >= 2
            ? double.Parse(
                args[1],
                System.Globalization.CultureInfo.InvariantCulture)
            : 5;
        if (seconds is <= 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args),
                "Capture duration must be greater than zero and no more than ten seconds.");
        }

        string model = args.Length >= 3 ? args[2] : "base.en";
        string? deviceId = args.Length == 4 ? args[3] : null;
        LiveCaptureProbe probe = await CaptureLivePcmAsync(seconds, deviceId)
            .ConfigureAwait(false);
        CapturedAudio audio = probe.Audio;
        string transcript = string.Empty;

        if (!audio.IsSilent && !audio.Pcm16.IsEmpty)
        {
            using var manager = new WhisperModelManager(
                WhisperModelStorage.ResolveDefaultDirectory());
            await using var engine = new WhisperTranscriptionEngine(manager);
            transcript = await engine.TranscribeAsync(
                    audio,
                    new(model, "en", WorkerThreads: 0),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        AudioSignalMetrics metrics = PcmAudioConverter.MeasureWhisperPcm16(
            audio.Pcm16.Span);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            audio.Duration,
            PcmBytes = audio.Pcm16.Length,
            audio.IsSilent,
            metrics.RootMeanSquare,
            metrics.Peak,
            probe.MaximumLevel,
            Transcript = transcript,
        }, JsonOptions));
        return string.IsNullOrWhiteSpace(transcript) ? 3 : 0;
    }

    private static async Task<LiveCaptureProbe> CaptureLivePcmAsync(
        double seconds,
        string? deviceId)
    {
        var service = new WasapiAudioCaptureService();
        IAudioCaptureSession session = await service.StartAsync(
                new(
                    deviceId,
                    SilenceThreshold: 0.012,
                    MaximumDuration: TimeSpan.FromSeconds(10)),
                CancellationToken.None)
            .ConfigureAwait(false);
        double maximumLevel = 0;
        if (session is IAudioLevelSource levelSource)
        {
            levelSource.LevelChanged += (_, level) =>
                maximumLevel = Math.Max(maximumLevel, level.Peak);
        }

        await using (session.ConfigureAwait(false))
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
            CapturedAudio audio = await session
                .StopAsync(CancellationToken.None)
                .ConfigureAwait(false);
            return new(audio, maximumLevel);
        }
    }

    private sealed record LiveCaptureProbe(
        CapturedAudio Audio,
        double MaximumLevel);

    private static async Task<int> TranscribeAsync(string[] args)
    {
        if (args.Length is < 3 or > 4)
        {
            return ShowUsage();
        }

        string modelDirectory = args.Length == 4
            ? args[3]
            : WhisperModelStorage.ResolveDefaultDirectory();
        CapturedAudio audio = await AudioFileLoader
            .LoadAsync(args[1])
            .ConfigureAwait(false);
        using var manager = new WhisperModelManager(modelDirectory);
        await using var engine = new WhisperTranscriptionEngine(manager);
        string transcript = await engine.TranscribeAsync(
                audio,
                new(args[2], "en", WorkerThreads: 0),
                CancellationToken.None)
            .ConfigureAwait(false);
        Console.WriteLine(transcript);
        return string.IsNullOrWhiteSpace(transcript) ? 3 : 0;
    }

    private static async Task<int> SpeechRegressionAsync(string[] args)
    {
        if (args.Length is < 5 or > 6 ||
            !double.TryParse(
                args[4],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double maximumWordErrorRate) ||
            maximumWordErrorRate is < 0 or > 1)
        {
            return ShowUsage();
        }

        string expected = await File
            .ReadAllTextAsync(args[2])
            .ConfigureAwait(false);
        string modelDirectory = args.Length == 6
            ? args[5]
            : WhisperModelStorage.ResolveDefaultDirectory();
        CapturedAudio original = await AudioFileLoader
            .LoadAsync(args[1])
            .ConfigureAwait(false);
        using var manager = new WhisperModelManager(modelDirectory);
        await using var engine = new WhisperTranscriptionEngine(manager);
        var settings = new TranscriptionSettings(
            args[3],
            "en",
            WorkerThreads: 0);
        var cases = new Dictionary<string, CapturedAudio>(
            StringComparer.Ordinal)
        {
            ["original"] = original,
            ["padded-silence"] = AddSilence(original),
            ["clipped"] = AmplifyAndClip(original),
        };

        bool passed = true;
        foreach ((string name, CapturedAudio audio) in cases)
        {
            string transcript = await engine
                .TranscribeAsync(audio, settings, CancellationToken.None)
                .ConfigureAwait(false);
            TranscriptScore score = TranscriptScorer.Score(expected, transcript);
            Console.WriteLine(
                $"{name}: WER={score.WordErrorRate:P1}; {transcript}");
            passed &= score.WordErrorRate <= maximumWordErrorRate;
        }

        var silence = new CapturedAudio(
            new byte[16_000 * sizeof(short)],
            16_000,
            1,
            TimeSpan.FromSeconds(1),
            IsSilent: true);
        string silenceTranscript = await engine
            .TranscribeAsync(silence, settings, CancellationToken.None)
            .ConfigureAwait(false);
        Console.WriteLine(
            $"silence: {(silenceTranscript.Length == 0 ? "PASS" : "FAIL")}");
        passed &= silenceTranscript.Length == 0;
        return passed ? 0 : 5;
    }

    private static CapturedAudio AddSilence(CapturedAudio audio)
    {
        int paddingBytes = 16_000 * sizeof(short) / 2;
        var padded = new byte[checked(audio.Pcm16.Length + (paddingBytes * 2))];
        audio.Pcm16.CopyTo(padded.AsMemory(paddingBytes));
        return audio with
        {
            Pcm16 = padded,
            Duration = audio.Duration + TimeSpan.FromSeconds(1),
        };
    }

    private static CapturedAudio AmplifyAndClip(CapturedAudio audio)
    {
        byte[] clipped = audio.Pcm16.ToArray();
        for (int offset = 0; offset < clipped.Length; offset += sizeof(short))
        {
            int sample = BinaryPrimitives.ReadInt16LittleEndian(
                clipped.AsSpan(offset, sizeof(short)));
            short amplified = (short)Math.Clamp(
                sample * 3,
                short.MinValue,
                short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(
                clipped.AsSpan(offset, sizeof(short)),
                amplified);
        }

        return audio with { Pcm16 = clipped };
    }
}
