using System.Collections.Immutable;
using DictaClone.Core.Contracts;
using DictaClone.Core.Settings;

namespace DictaClone.App.Tests;

public sealed class TranscriptHistoryRecorderTests
{
    [Fact]
    public async Task DisabledHistory_DoesNotWriteTranscript()
    {
        var store = new FakeHistoryStore();
        var recorder = new TranscriptHistoryRecorder(store);

        bool recorded = await recorder.RecordIfEnabledAsync(
            "private final text",
            DictaCloneSettings.Default.Preferences,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.False(recorded);
        Assert.Empty(store.Appends);
    }

    [Fact]
    public async Task EnabledHistory_WritesFinalTextWithConfiguredLimit()
    {
        var store = new FakeHistoryStore();
        var recorder = new TranscriptHistoryRecorder(store);
        DateTimeOffset createdUtc = new(
            2026,
            8,
            1,
            12,
            30,
            0,
            TimeSpan.Zero);
        ApplicationPreferences preferences =
            DictaCloneSettings.Default.Preferences with
            {
                HistoryEnabled = true,
                HistoryLimit = 27,
            };

        bool recorded = await recorder.RecordIfEnabledAsync(
            "final text",
            preferences,
            createdUtc,
            CancellationToken.None);

        Assert.True(recorded);
        (TranscriptHistoryEntry entry, int limit) =
            Assert.Single(store.Appends);
        Assert.Equal(createdUtc, entry.CreatedUtc);
        Assert.Equal("final text", entry.Text);
        Assert.Equal(27, limit);
    }

    private sealed class FakeHistoryStore : ITranscriptHistoryStore
    {
        public List<(TranscriptHistoryEntry Entry, int Limit)> Appends
        {
            get;
        } = [];

        public Task<HistoryLoadResult> LoadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new HistoryLoadResult(
                ImmutableArray<TranscriptHistoryEntry>.Empty));

        public Task AppendAsync(
            TranscriptHistoryEntry entry,
            int maximumEntries,
            CancellationToken cancellationToken)
        {
            Appends.Add((entry, maximumEntries));
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
