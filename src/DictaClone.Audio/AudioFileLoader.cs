using DictaClone.Core.Dictation;
using NAudio.Wave;

namespace DictaClone.Audio;

public static class AudioFileLoader
{
    public static async Task<CapturedAudio> LoadAsync(
        string wavePath,
        double silenceThreshold = 0.012,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wavePath);
        if (!File.Exists(wavePath))
        {
            throw new FileNotFoundException(
                "The WAV audio file was not found.",
                wavePath);
        }

        await using var file = new FileStream(
            wavePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new WaveFileReader(file);
        using var audio = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;

        while ((read = await reader.ReadAsync(
                   buffer,
                   cancellationToken)
               .ConfigureAwait(false)) > 0)
        {
            await audio.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return PcmAudioConverter.ConvertToWhisperPcm16(
            audio.ToArray(),
            reader.WaveFormat,
            silenceThreshold);
    }
}
