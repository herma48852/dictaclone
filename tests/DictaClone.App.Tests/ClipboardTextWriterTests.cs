using System.Runtime.InteropServices;
using DictaClone.App.Presentation;

namespace DictaClone.App.Tests;

public sealed class ClipboardTextWriterTests
{
    [Fact]
    public async Task TryWrite_RetriesTransientClipboardContention()
    {
        int failuresRemaining = 6;
        int writes = 0;
        var delays = new List<TimeSpan>();
        var writer = new ClipboardTextWriter(
            _ =>
            {
                writes++;
                if (failuresRemaining-- > 0)
                {
                    throw new FakeClipboardBusyException();
                }
            },
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        bool copied = await writer.TryWriteAsync(
            "recovered",
            CancellationToken.None);

        Assert.True(copied);
        Assert.Equal(7, writes);
        Assert.Equal(6, delays.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(25), delays[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(150), delays[^1]);
    }

    [Fact]
    public async Task TryWrite_ReportsPersistentClipboardContention()
    {
        int writes = 0;
        var writer = new ClipboardTextWriter(
            _ =>
            {
                writes++;
                throw new FakeClipboardBusyException();
            },
            (_, _) => Task.CompletedTask,
            attempts: 3,
            retryDelay: TimeSpan.Zero);

        bool copied = await writer.TryWriteAsync(
            "blocked",
            CancellationToken.None);

        Assert.False(copied);
        Assert.Equal(3, writes);
    }

    private sealed class FakeClipboardBusyException()
        : ExternalException("busy");
}
