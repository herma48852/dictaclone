using DictaClone.Core.Contracts;
using DictaClone.Core.Settings;

namespace DictaClone.App;

internal sealed class TranscriptHistoryRecorder
{
    private readonly ITranscriptHistoryStore _store;

    public TranscriptHistoryRecorder(ITranscriptHistoryStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<bool> RecordIfEnabledAsync(
        string transcript,
        ApplicationPreferences preferences,
        DateTimeOffset createdUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcript);
        ArgumentNullException.ThrowIfNull(preferences);
        if (!preferences.HistoryEnabled)
        {
            return false;
        }

        await _store.AppendAsync(
            new(createdUtc, transcript),
            preferences.HistoryLimit,
            cancellationToken).ConfigureAwait(false);
        return true;
    }
}
