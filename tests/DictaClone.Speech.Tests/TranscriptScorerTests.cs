using DictaClone.Speech;

namespace DictaClone.Speech.Tests;

public sealed class TranscriptScorerTests
{
    [Fact]
    public void Score_IgnoresCaseAndPunctuation()
    {
        TranscriptScore score = TranscriptScorer.Score(
            "Hello, Windows 11!",
            "hello windows 11");

        Assert.Equal(0, score.EditDistance);
        Assert.Equal(0, score.WordErrorRate);
    }

    [Fact]
    public void Score_CountsInsertionsDeletionsAndSubstitutions()
    {
        TranscriptScore score = TranscriptScorer.Score(
            "one two three four",
            "one too four extra");

        Assert.Equal(3, score.EditDistance);
        Assert.Equal(0.75, score.WordErrorRate);
    }

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("", "unexpected", 1)]
    public void Score_HandlesEmptyReference(
        string expected,
        string actual,
        double expectedWordErrorRate)
    {
        TranscriptScore score = TranscriptScorer.Score(expected, actual);

        Assert.Equal(expectedWordErrorRate, score.WordErrorRate);
    }
}
