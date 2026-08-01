using System.Security.Cryptography;
using DictaClone.Audio;
using NAudio.Wave;

namespace DictaClone.IntegrationTests;

public sealed class AudioFixtureTests
{
    private const string ExpectedFixtureHash =
        "59DFB9A4ACB36FE2A2AFFC14BACBEE2920FF435CB13CC314A08C13F66BA7860E";

    [Fact]
    public void JfkFixtureIsStableAndReadable()
    {
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "audio",
            "jfk.wav");

        Assert.True(File.Exists(fixturePath));

        using (var waveReader = new WaveFileReader(fixturePath))
        {
            Assert.Equal(TimeSpan.FromSeconds(11), waveReader.TotalTime);
        }

        using FileStream fixtureStream = File.OpenRead(fixturePath);
        string actualHash = Convert.ToHexString(SHA256.HashData(fixtureStream));
        Assert.Equal(ExpectedFixtureHash, actualHash);
    }

    [Fact]
    public async Task JfkFixtureLoadsAsWhisperPcmEntirelyInMemory()
    {
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "audio",
            "jfk.wav");

        var audio = await AudioFileLoader.LoadAsync(fixturePath);

        Assert.Equal(16_000, audio.SampleRate);
        Assert.Equal(1, audio.ChannelCount);
        Assert.Equal(TimeSpan.FromSeconds(11), audio.Duration);
        Assert.Equal(352_000, audio.Pcm16.Length);
        Assert.False(audio.IsSilent);
    }
}
