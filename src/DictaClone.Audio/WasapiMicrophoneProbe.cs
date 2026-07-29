using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DictaClone.Audio;

public sealed class WasapiMicrophoneProbe
{
    public static IReadOnlyList<MicrophoneDeviceInfo> GetActiveCaptureDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var defaultDevice = enumerator.GetDefaultAudioEndpoint(
            DataFlow.Capture,
            Role.Multimedia);

        string defaultId = defaultDevice.ID;

        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(device => new MicrophoneDeviceInfo(
                device.ID,
                device.FriendlyName,
                string.Equals(device.ID, defaultId, StringComparison.Ordinal)))
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static async Task<CaptureProbeResult> CaptureAsync(
        TimeSpan duration,
        string? deviceId = null,
        CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromSeconds(10))
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "The capture probe duration must be greater than zero and no more than ten seconds.");
        }

        using var enumerator = new MMDeviceEnumerator();
        using var device = string.IsNullOrWhiteSpace(deviceId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
            : enumerator.GetDevice(deviceId);
        using var capture = new WasapiCapture(device);

        long bytesCaptured = 0;
        int bufferCount = 0;
        var stopped = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        capture.DataAvailable += (_, eventArgs) =>
        {
            Interlocked.Add(ref bytesCaptured, eventArgs.BytesRecorded);
            Interlocked.Increment(ref bufferCount);
        };
        capture.RecordingStopped += (_, eventArgs) => stopped.TrySetResult(eventArgs.Exception);

        var stopwatch = Stopwatch.StartNew();
        capture.StartRecording();

        try
        {
            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            capture.StopRecording();
        }

        Exception? captureException = await stopped.Task
            .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
            .ConfigureAwait(false);
        stopwatch.Stop();

        if (captureException is not null)
        {
            throw new InvalidOperationException("WASAPI stopped with an error.", captureException);
        }

        return new CaptureProbeResult(
            device.ID,
            device.FriendlyName,
            capture.WaveFormat.ToString(),
            duration,
            stopwatch.Elapsed,
            Interlocked.Read(ref bytesCaptured),
            Volatile.Read(ref bufferCount));
    }
}
