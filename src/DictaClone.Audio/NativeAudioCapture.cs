using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DictaClone.Audio;

internal interface INativeAudioCapture : IDisposable
{
    WaveFormat WaveFormat { get; }

    event EventHandler<NativeAudioDataEventArgs>? DataAvailable;

    event EventHandler<NativeAudioStoppedEventArgs>? RecordingStopped;

    void StartRecording();

    void StopRecording();
}

internal interface INativeAudioCaptureFactory
{
    INativeAudioCapture Create(string? deviceId);
}

internal sealed class WasapiNativeAudioCaptureFactory : INativeAudioCaptureFactory
{
    public INativeAudioCapture Create(string? deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice device = string.IsNullOrWhiteSpace(deviceId)
            ? enumerator.GetDefaultAudioEndpoint(
                DataFlow.Capture,
                Role.Multimedia)
            : enumerator.GetDevice(deviceId);

        try
        {
            return new WasapiNativeAudioCapture(device);
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }
}

internal sealed class WasapiNativeAudioCapture : INativeAudioCapture
{
    private readonly MMDevice _device;
    private readonly WasapiCapture _capture;

    public WasapiNativeAudioCapture(MMDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _capture = new(device);
        _capture.DataAvailable += CaptureDataAvailable;
        _capture.RecordingStopped += CaptureRecordingStopped;
    }

    public WaveFormat WaveFormat => _capture.WaveFormat;

    public event EventHandler<NativeAudioDataEventArgs>? DataAvailable;

    public event EventHandler<NativeAudioStoppedEventArgs>? RecordingStopped;

    public void StartRecording() => _capture.StartRecording();

    public void StopRecording() => _capture.StopRecording();

    public void Dispose()
    {
        _capture.DataAvailable -= CaptureDataAvailable;
        _capture.RecordingStopped -= CaptureRecordingStopped;
        _capture.Dispose();
        _device.Dispose();
    }

    private void CaptureDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        DataAvailable?.Invoke(
            this,
            new(eventArgs.Buffer.AsMemory(0, eventArgs.BytesRecorded)));
    }

    private void CaptureRecordingStopped(
        object? sender,
        StoppedEventArgs eventArgs)
    {
        RecordingStopped?.Invoke(this, new(eventArgs.Exception));
    }
}

internal sealed class NativeAudioDataEventArgs(ReadOnlyMemory<byte> data)
    : EventArgs
{
    public ReadOnlyMemory<byte> Data { get; } = data;
}

internal sealed class NativeAudioStoppedEventArgs(Exception? exception)
    : EventArgs
{
    public Exception? Exception { get; } = exception;
}
