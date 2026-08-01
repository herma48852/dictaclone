using DictaClone.App.Presentation;
using DictaClone.Core.Contracts;
using DictaClone.Core.Hotkeys;

namespace DictaClone.App.Tests;

public sealed class TriggerUiControllerTests
{
    [Fact]
    public async Task HoldTrigger_ShowsRecordingProcessingAndSuccess()
    {
        var overlay = new FakeOverlay();
        using var controller = new TriggerUiController(
            overlay,
            (_, _) => Task.CompletedTask);

        await controller.HandleAsync(new(
            HotkeyAction.Dictation,
            HotkeyEventKind.Pressed,
            IsInjected: false));
        await controller.HandleAsync(new(
            HotkeyAction.Dictation,
            HotkeyEventKind.Released,
            IsInjected: false));

        Assert.Equal(
            [
                OverlayStatus.Recording,
                OverlayStatus.Processing,
                OverlayStatus.Success,
            ],
            overlay.Statuses);
        Assert.Contains("Shortcut detected", overlay.Messages[^1]);
    }

    [Fact]
    public async Task TriggerLabels_DistinguishAvailableModes()
    {
        var overlay = new FakeOverlay();
        using var controller = new TriggerUiController(overlay);

        await controller.HandleAsync(new(
            HotkeyAction.SmartEdit,
            HotkeyEventKind.Pressed,
            IsInjected: false));
        await controller.HandleAsync(new(
            HotkeyAction.TypingMode,
            HotkeyEventKind.Pressed,
            IsInjected: false));

        Assert.Contains("Smart Edit", overlay.Messages[0]);
        Assert.Contains("Typing Mode", overlay.Messages[1]);
    }

    [Fact]
    public async Task CancelTrigger_ShowsCancellationStatus()
    {
        var overlay = new FakeOverlay();
        using var controller = new TriggerUiController(overlay);

        await controller.HandleAsync(new(
            HotkeyAction.Cancel,
            HotkeyEventKind.Pressed,
            IsInjected: false));

        Assert.Equal(OverlayStatus.Failure, Assert.Single(overlay.Statuses));
        Assert.Contains("cancelled", Assert.Single(overlay.Messages));
    }

    [Fact]
    public async Task NewTrigger_CancelsPendingSuccessPreview()
    {
        var overlay = new FakeOverlay();
        using var controller = new TriggerUiController(
            overlay,
            static (_, cancellationToken) =>
                Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));

        Task pending = controller.HandleAsync(new(
            HotkeyAction.Dictation,
            HotkeyEventKind.Released,
            IsInjected: false));
        await controller.HandleAsync(new(
            HotkeyAction.Dictation,
            HotkeyEventKind.Pressed,
            IsInjected: false));
        await pending;

        Assert.Equal(OverlayStatus.Recording, overlay.Statuses[^1]);
        Assert.DoesNotContain(OverlayStatus.Success, overlay.Statuses);
    }

    [Fact]
    public void Constructor_RejectsNullOverlay()
    {
        Assert.Throws<ArgumentNullException>(
            () => new TriggerUiController(null!));
    }

    private sealed class FakeOverlay : IStatusOverlay
    {
        public List<OverlayStatus> Statuses { get; } = [];

        public List<string> Messages { get; } = [];

        public void ShowStatus(OverlayStatus status, string? message = null)
        {
            Statuses.Add(status);
            Messages.Add(message ?? string.Empty);
        }

        public void HideStatus()
        {
        }
    }
}
