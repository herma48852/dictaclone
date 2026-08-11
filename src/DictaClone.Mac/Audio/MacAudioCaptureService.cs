using System.Buffers.Binary;
using System.Runtime.InteropServices;
using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;
using DictaClone.Core.Settings;

namespace DictaClone.Mac.Audio;

public sealed class MacAudioCaptureService : IAudioCaptureService
{
    private readonly IMacAudioQueueFactory _factory;

    public MacAudioCaptureService()
        : this(new NativeMacAudioQueueFactory())
    {
    }

    internal MacAudioCaptureService(IMacAudioQueueFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public Task<IAudioCaptureSession> StartAsync(
        AudioSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            IMacAudioQueue queue = _factory.Create(settings.DeviceId);
            var session = new MacAudioCaptureSession(queue, settings);
            try
            {
                session.Start();
                return Task.FromResult<IAudioCaptureSession>(session);
            }
            catch
            {
                queue.Dispose();
                throw;
            }
        }
        catch (AudioCaptureException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AudioCaptureException(
                "The selected macOS microphone could not be opened.",
                exception);
        }
    }
}

internal interface IMacAudioQueueFactory
{
    IMacAudioQueue Create(string? deviceId);
}

internal interface IMacAudioQueue : IDisposable
{
    event EventHandler<MacAudioDataEventArgs>? DataAvailable;

    void Start();

    void Stop();
}

internal sealed class MacAudioDataEventArgs(ReadOnlyMemory<byte> pcm16)
    : EventArgs
{
    public ReadOnlyMemory<byte> Pcm16 { get; } = pcm16;
}

internal sealed class MacAudioCaptureSession :
    IAudioCaptureSession,
    IAudioLevelSource
{
    private const int SampleRate = 16_000;
    private readonly object _sync = new();
    private readonly IMacAudioQueue _queue;
    private readonly AudioSettings _settings;
    private readonly MemoryStream _audio = new();
    private readonly long _maximumBytes;
    private Task<CapturedAudio>? _stopTask;
    private bool _discard;
    private bool _disposed;
    private bool _started;

    public MacAudioCaptureSession(
        IMacAudioQueue queue,
        AudioSettings settings)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _maximumBytes = checked((long)Math.Ceiling(
            settings.MaximumDuration.TotalSeconds * SampleRate * sizeof(short)));
        _queue.DataAvailable += DataAvailable;
    }

    public event EventHandler<AudioLevelChangedEvent>? LevelChanged;

    public void Start()
    {
        try
        {
            _queue.Start();
            _started = true;
        }
        catch (Exception exception)
        {
            throw new AudioCaptureException(
                "macOS microphone capture could not start. Check Microphone permission in System Settings.",
                exception);
        }
    }

    public Task<CapturedAudio> StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _stopTask ??= Task.Run(CompleteStop);
            return _stopTask.WaitAsync(cancellationToken);
        }
    }

    public async Task CancelAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _discard = true;
        }

        _ = await StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await CancelAsync(CancellationToken.None).ConfigureAwait(false);
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _queue.DataAvailable -= DataAvailable;
        _queue.Dispose();
        _audio.Dispose();
    }

    private CapturedAudio CompleteStop()
    {
        try
        {
            if (_started)
            {
                _queue.Stop();
                _started = false;
            }

            byte[] pcm;
            bool discard;
            lock (_sync)
            {
                pcm = _audio.ToArray();
                discard = _discard;
            }

            return discard
                ? EmptyAudio()
                : CreateAudio(pcm, _settings.SilenceThreshold);
        }
        catch (Exception exception)
        {
            throw new AudioCaptureException(
                "The microphone was disconnected or stopped unexpectedly.",
                exception);
        }
    }

    private void DataAvailable(object? sender, MacAudioDataEventArgs eventArgs)
    {
        ReadOnlySpan<byte> source = eventArgs.Pcm16.Span;
        int bytesToWrite;
        lock (_sync)
        {
            if (_discard || !_started || _audio.Length >= _maximumBytes)
            {
                return;
            }

            bytesToWrite = checked((int)Math.Min(
                source.Length,
                _maximumBytes - _audio.Length));
            bytesToWrite -= bytesToWrite % sizeof(short);
            if (bytesToWrite > 0)
            {
                _audio.Write(source[..bytesToWrite]);
            }
        }

        if (bytesToWrite > 0)
        {
            (double rms, double peak) = Measure(source[..bytesToWrite]);
            LevelChanged?.Invoke(this, new(rms, peak));
        }
    }

    private static CapturedAudio CreateAudio(
        byte[] pcm,
        double silenceThreshold)
    {
        const int windowSamples = SampleRate * 20 / 1000;
        int sampleCount = pcm.Length / sizeof(short);
        int activeSamples = 0;
        double peak = 0;

        for (int start = 0; start < sampleCount; start += windowSamples)
        {
            int count = Math.Min(windowSamples, sampleCount - start);
            double sumSquares = 0;
            for (int offset = 0; offset < count; offset++)
            {
                short encoded = BinaryPrimitives.ReadInt16LittleEndian(
                    pcm.AsSpan((start + offset) * sizeof(short), sizeof(short)));
                double sample = encoded / 32768d;
                peak = Math.Max(peak, Math.Abs(sample));
                sumSquares += sample * sample;
            }

            if (count > 0 && Math.Sqrt(sumSquares / count) >= silenceThreshold)
            {
                activeSamples += count;
            }
        }

        TimeSpan duration = TimeSpan.FromSeconds(sampleCount / (double)SampleRate);
        TimeSpan activeDuration = TimeSpan.FromSeconds(
            activeSamples / (double)SampleRate);
        bool silent = duration < TimeSpan.FromMilliseconds(150) ||
            activeDuration < TimeSpan.FromMilliseconds(150) ||
            peak == 0;
        return new(pcm, SampleRate, ChannelCount: 1, duration, silent);
    }

    private static (double Rms, double Peak) Measure(ReadOnlySpan<byte> pcm)
    {
        int sampleCount = pcm.Length / sizeof(short);
        if (sampleCount == 0)
        {
            return (0, 0);
        }

        double sumSquares = 0;
        double peak = 0;
        for (int index = 0; index < sampleCount; index++)
        {
            short encoded = BinaryPrimitives.ReadInt16LittleEndian(
                pcm.Slice(index * sizeof(short), sizeof(short)));
            double sample = encoded / 32768d;
            sumSquares += sample * sample;
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return (Math.Sqrt(sumSquares / sampleCount), peak);
    }

    private static CapturedAudio EmptyAudio() => new(
        ReadOnlyMemory<byte>.Empty,
        SampleRate,
        ChannelCount: 1,
        TimeSpan.Zero,
        IsSilent: true);
}

internal sealed partial class NativeMacAudioQueueFactory : IMacAudioQueueFactory
{
    public IMacAudioQueue Create(string? deviceId) =>
        new NativeMacAudioQueue(deviceId);
}

internal sealed partial class NativeMacAudioQueue : IMacAudioQueue
{
    private const uint LinearPcm = 0x6C70636D;
    private const uint SignedInteger = 1U << 2;
    private const uint Packed = 1U << 3;
    private const int BufferSize = 8 * 1024;
    private const int BufferCount = 3;
    private const uint CurrentDeviceProperty = 0x61716364;
    private readonly object _sync = new();
    private readonly AudioQueueInputCallback _callback;
    private readonly List<nint> _buffers = [];
    private nint _queue;
    private bool _running;
    private bool _disposed;

    public NativeMacAudioQueue(string? deviceId)
    {
        _callback = AudioInput;
        var format = new AudioStreamBasicDescription
        {
            SampleRate = 16_000,
            FormatId = LinearPcm,
            FormatFlags = SignedInteger | Packed,
            BytesPerPacket = sizeof(short),
            FramesPerPacket = 1,
            BytesPerFrame = sizeof(short),
            ChannelsPerFrame = 1,
            BitsPerChannel = 16,
        };
        ThrowIfError(AudioQueueNewInput(
            ref format,
            _callback,
            nint.Zero,
            nint.Zero,
            nint.Zero,
            flags: 0,
            out _queue),
            "AudioQueueNewInput");

        try
        {
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                SetCurrentDevice(deviceId);
            }

            for (int index = 0; index < BufferCount; index++)
            {
                ThrowIfError(
                    AudioQueueAllocateBuffer(_queue, BufferSize, out nint buffer),
                    "AudioQueueAllocateBuffer");
                _buffers.Add(buffer);
                ThrowIfError(
                    AudioQueueEnqueueBuffer(
                        _queue,
                        buffer,
                        packetDescriptionCount: 0,
                        nint.Zero),
                    "AudioQueueEnqueueBuffer");
            }
        }
        catch
        {
            _ = AudioQueueDispose(_queue, immediate: true);
            _queue = nint.Zero;
            throw;
        }
    }

    public event EventHandler<MacAudioDataEventArgs>? DataAvailable;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_sync)
        {
            _running = true;
        }

        try
        {
            ThrowIfError(AudioQueueStart(_queue, nint.Zero), "AudioQueueStart");
        }
        catch
        {
            lock (_sync)
            {
                _running = false;
            }

            throw;
        }
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_sync)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
        }

        ThrowIfError(
            AudioQueueStop(_queue, immediate: true),
            "AudioQueueStop");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            _running = false;
            _disposed = true;
        }

        if (_queue != nint.Zero)
        {
            _ = AudioQueueDispose(_queue, immediate: true);
            _queue = nint.Zero;
        }
    }

    private void AudioInput(
        nint userData,
        nint queue,
        nint buffer,
        nint startTime,
        uint packetCount,
        nint packetDescriptions)
    {
        try
        {
            AudioQueueBuffer nativeBuffer = Marshal.PtrToStructure<AudioQueueBuffer>(
                buffer);
            if (nativeBuffer.AudioDataByteSize > 0)
            {
                var data = new byte[checked((int)nativeBuffer.AudioDataByteSize)];
                Marshal.Copy(
                    nativeBuffer.AudioData,
                    data,
                    startIndex: 0,
                    data.Length);
                DataAvailable?.Invoke(this, new(data));
            }

            bool requeue;
            lock (_sync)
            {
                requeue = _running && !_disposed;
            }

            if (requeue)
            {
                _ = AudioQueueEnqueueBuffer(
                    queue,
                    buffer,
                    packetDescriptionCount: 0,
                    nint.Zero);
            }
        }
        catch (Exception)
        {
            // The managed session reports an empty/failed capture after stop.
        }
    }

    private static void ThrowIfError(int status, string operation)
    {
        if (status != 0)
        {
            throw new AudioCaptureException(
                $"{operation} failed with Core Audio status {FormatStatus(status)}.",
                new MacAudioQueueException(operation, status));
        }
    }

    private void SetCurrentDevice(string deviceId)
    {
        nint deviceUid = Interop.ObjectiveC.CreateString(deviceId);
        try
        {
            ThrowIfError(
                AudioQueueSetProperty(
                    _queue,
                    CurrentDeviceProperty,
                    ref deviceUid,
                    checked((uint)IntPtr.Size)),
                "AudioQueueSetProperty(CurrentDevice)");
        }
        finally
        {
            Interop.MacNative.CFRelease(deviceUid);
        }
    }

    private static string FormatStatus(int status)
    {
        uint value = unchecked((uint)status);
        Span<char> characters = stackalloc char[4]
        {
            (char)((value >> 24) & 0xFF),
            (char)((value >> 16) & 0xFF),
            (char)((value >> 8) & 0xFF),
            (char)(value & 0xFF),
        };
        bool printable = true;
        foreach (char character in characters)
        {
            printable &= character is >= ' ' and <= '~';
        }

        return printable
            ? $"'{new string(characters)}'"
            : status.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioStreamBasicDescription
    {
        public double SampleRate;
        public uint FormatId;
        public uint FormatFlags;
        public uint BytesPerPacket;
        public uint FramesPerPacket;
        public uint BytesPerFrame;
        public uint ChannelsPerFrame;
        public uint BitsPerChannel;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioQueueBuffer
    {
        public uint AudioDataBytesCapacity;
        public nint AudioData;
        public uint AudioDataByteSize;
        public nint UserData;
        public uint PacketDescriptionCapacity;
        public uint PacketDescriptionCount;
        public nint PacketDescriptions;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AudioQueueInputCallback(
        nint userData,
        nint queue,
        nint buffer,
        nint startTime,
        uint packetCount,
        nint packetDescriptions);

    [LibraryImport(
        "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static partial int AudioQueueNewInput(
        ref AudioStreamBasicDescription format,
        AudioQueueInputCallback callback,
        nint userData,
        nint callbackRunLoop,
        nint callbackRunLoopMode,
        uint flags,
        out nint queue);

    [LibraryImport(
        "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static partial int AudioQueueAllocateBuffer(
        nint queue,
        uint bufferByteSize,
        out nint buffer);

    [LibraryImport(
        "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static partial int AudioQueueEnqueueBuffer(
        nint queue,
        nint buffer,
        uint packetDescriptionCount,
        nint packetDescriptions);

    [LibraryImport(
        "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static partial int AudioQueueStart(
        nint queue,
        nint startTime);

    [LibraryImport(
        "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static partial int AudioQueueStop(
        nint queue,
        [MarshalAs(UnmanagedType.I1)] bool immediate);

    [LibraryImport(
        "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static partial int AudioQueueDispose(
        nint queue,
        [MarshalAs(UnmanagedType.I1)] bool immediate);

    [LibraryImport(
        "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static partial int AudioQueueSetProperty(
        nint queue,
        uint propertyId,
        ref nint propertyData,
        uint propertyDataSize);
}

internal sealed class MacAudioQueueException(string operation, int status)
    : Exception($"{operation} failed with status {status}.")
{
    public int Status { get; } = status;
}
