using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;

namespace DictaClone.App.Presentation;

public sealed partial class StatusOverlayWindow : Window, IStatusOverlay
{
    private const int ExtendedStyleIndex = -20;
    private const long NoActivateStyle = 0x08000000L;
    private const long ToolWindowStyle = 0x00000080L;
    private const long TransparentStyle = 0x00000020L;
    private const int MouseActivateMessage = 0x0021;
    private const nint MouseNoActivateResult = 3;
    private const uint NoActivatePositionFlag = 0x0010;
    private const uint NoOwnerOrderFlag = 0x0200;
    private const uint NoSizeFlag = 0x0001;
    private const uint NoMoveFlag = 0x0002;
    private static readonly nint TopmostWindow = new(-1);

    private readonly Border _pill;
    private readonly TextBlock _label;
    private readonly DispatcherTimer _hideTimer;

    public StatusOverlayWindow()
    {
        AllowsTransparency = true;
        Background = MediaBrushes.Transparent;
        Focusable = false;
        IsHitTestVisible = false;
        ShowActivated = false;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        WindowStyle = WindowStyle.None;

        _label = new()
        {
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = MediaBrushes.White,
            Margin = new(18, 9, 18, 10),
        };
        _pill = new()
        {
            Background = CreateBrush(0xCC, 0x16, 0x18, 0x1D),
            BorderBrush = CreateBrush(0x80, 0xFF, 0xFF, 0xFF),
            BorderThickness = new(1),
            CornerRadius = new(22),
            Child = _label,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                Opacity = 0.35,
                ShadowDepth = 3,
            },
        };
        Content = _pill;

        _hideTimer = new()
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _hideTimer.Tick += (_, _) => HideStatus();
    }

    public void ShowStatus(OverlayStatus status, string? message = null)
    {
        Dispatcher.VerifyAccess();
        _hideTimer.Stop();
        (_pill.Background, _label.Text) = GetAppearance(status, message);

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        PositionAtBottomCenter();
        EnsureTopmostWithoutActivation();

        if (status is OverlayStatus.Success or OverlayStatus.Failure)
        {
            _hideTimer.Interval = status == OverlayStatus.Success
                ? TimeSpan.FromSeconds(1.6)
                : TimeSpan.FromSeconds(3);
            _hideTimer.Start();
        }
    }

    public void HideStatus()
    {
        Dispatcher.VerifyAccess();
        _hideTimer.Stop();
        Hide();
    }

    public bool HasNoActivateExtendedStyle
    {
        get
        {
            nint handle = new WindowInteropHelper(this).Handle;
            if (handle == nint.Zero)
            {
                return false;
            }

            nint styles = NativeMethods.GetWindowLongPtr(
                handle,
                ExtendedStyleIndex);
            return (styles.ToInt64() & NoActivateStyle) != 0;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        nint handle = new WindowInteropHelper(this).Handle;
        nint styles = NativeMethods.GetWindowLongPtr(handle, ExtendedStyleIndex);
        nint noActivateStyles = new(
            styles.ToInt64() |
            NoActivateStyle |
            ToolWindowStyle |
            TransparentStyle);
        _ = NativeMethods.SetWindowLongPtr(
            handle,
            ExtendedStyleIndex,
            noActivateStyles);

        if (HwndSource.FromHwnd(handle) is { } source)
        {
            source.AddHook(WindowProcedure);
        }

        EnsureTopmostWithoutActivation();
    }

    protected override void OnClosed(EventArgs e)
    {
        _hideTimer.Stop();
        base.OnClosed(e);
    }

    private static (MediaBrush Background, string Label) GetAppearance(
        OverlayStatus status,
        string? message)
    {
        string fallback = status switch
        {
            OverlayStatus.Recording => "●  Listening…",
            OverlayStatus.Processing => "Working…",
            OverlayStatus.Success => "✓  Ready",
            OverlayStatus.Failure => "!  DictaClone needs attention",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
        MediaBrush background = status switch
        {
            OverlayStatus.Recording => CreateBrush(0xEE, 0xB9, 0x1C, 0x3C),
            OverlayStatus.Processing => CreateBrush(0xEE, 0x27, 0x4A, 0xA8),
            OverlayStatus.Success => CreateBrush(0xEE, 0x19, 0x7A, 0x4A),
            OverlayStatus.Failure => CreateBrush(0xEE, 0x9D, 0x2B, 0x20),
            _ => MediaBrushes.Black,
        };

        return (background, string.IsNullOrWhiteSpace(message) ? fallback : message);
    }

    private void PositionAtBottomCenter()
    {
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Left + ((workArea.Width - ActualWidth) / 2);
        Top = workArea.Bottom - ActualHeight - 42;
    }

    private void EnsureTopmostWithoutActivation()
    {
        nint handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        _ = NativeMethods.SetWindowPos(
            handle,
            TopmostWindow,
            0,
            0,
            0,
            0,
            NoActivatePositionFlag |
            NoOwnerOrderFlag |
            NoSizeFlag |
            NoMoveFlag);
    }

    private static nint WindowProcedure(
        nint window,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (message == MouseActivateMessage)
        {
            handled = true;
            return MouseNoActivateResult;
        }

        return nint.Zero;
    }

    private static SolidColorBrush CreateBrush(
        byte alpha,
        byte red,
        byte green,
        byte blue)
    {
        var brush = new SolidColorBrush(
            MediaColor.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        internal static partial nint GetWindowLongPtr(
            nint window,
            int index);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static partial nint SetWindowLongPtr(
            nint window,
            int index,
            nint newValue);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetWindowPos(
            nint window,
            nint insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);
    }
}
