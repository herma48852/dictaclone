using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using DictaClone.App.Presentation;

namespace DictaClone.Mac.Presentation;

public sealed class MacStatusOverlayWindow : Window, IStatusOverlay
{
    private readonly Border _surface;
    private readonly TextBlock _message;
    private readonly ProgressBar _level;
    private readonly DispatcherTimer _hideTimer;

    public MacStatusOverlayWindow()
    {
        Width = 440;
        Height = 76;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _message = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        _level = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Height = 5,
            Margin = new Thickness(0, 8, 0, 0),
            IsVisible = false,
        };
        _surface = new Border
        {
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18, 12),
            Child = new StackPanel
            {
                Children =
                {
                    _message,
                    _level,
                },
            },
        };
        Content = _surface;
        Opened += (_, _) => PositionOnPrimaryScreen();
        _hideTimer = new DispatcherTimer();
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            HideStatus();
        };
    }

    public void ShowStatus(OverlayStatus status, string? message = null)
    {
        _hideTimer.Stop();
        _message.Text = message ?? status switch
        {
            OverlayStatus.Recording => "●  Listening…",
            OverlayStatus.Processing => "Working…",
            OverlayStatus.Success => "✓  DictaClone is ready",
            OverlayStatus.Failure => "DictaClone needs attention",
            _ => "DictaClone",
        };
        _surface.Background = new SolidColorBrush(status switch
        {
            OverlayStatus.Recording => Color.FromArgb(242, 185, 28, 60),
            OverlayStatus.Processing => Color.FromArgb(242, 49, 87, 200),
            OverlayStatus.Success => Color.FromArgb(242, 25, 122, 74),
            OverlayStatus.Failure => Color.FromArgb(242, 157, 43, 32),
            _ => Color.FromArgb(242, 49, 87, 200),
        });
        _level.IsVisible = status == OverlayStatus.Recording;
        if (!IsVisible)
        {
            Show();
        }

        PositionOnPrimaryScreen();
        if (status is OverlayStatus.Success or OverlayStatus.Failure)
        {
            _hideTimer.Interval = status == OverlayStatus.Success
                ? TimeSpan.FromSeconds(2.5)
                : TimeSpan.FromSeconds(6);
            _hideTimer.Start();
        }
    }

    public void HideStatus()
    {
        _hideTimer.Stop();
        Hide();
    }

    public void UpdateLevel(double level) =>
        _level.Value = Math.Clamp(level, 0, 1);

    private void PositionOnPrimaryScreen()
    {
        Screen? screen = Screens.Primary;
        if (screen is null)
        {
            return;
        }

        PixelRect workingArea = screen.WorkingArea;
        Position = new PixelPoint(
            workingArea.X + (workingArea.Width - checked((int)Width)) / 2,
            workingArea.Bottom - checked((int)Height) - 28);
    }
}
