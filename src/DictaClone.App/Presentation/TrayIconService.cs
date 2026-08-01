using System.Drawing;
using Forms = System.Windows.Forms;

namespace DictaClone.App.Presentation;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private bool _disposed;

    public TrayIconService()
    {
        _menu = new();
        _menu.Items.Add(
            "Open settings",
            image: null,
            (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(
            "Exit DictaClone",
            image: null,
            (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new()
        {
            ContextMenuStrip = _menu,
            Icon = SystemIcons.Application,
            Text = "DictaClone",
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) =>
            SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

    public void ShowNotification(
        string title,
        string message,
        Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(2500);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _disposed = true;
    }
}
