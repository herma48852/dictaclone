using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace DictaClone.Mac.Presentation;

public sealed class MacTrayIconService : IDisposable
{
    private readonly TrayIcon _trayIcon;
    private readonly TrayIcons _icons;
    private readonly Application _application;
    private bool _disposed;

    public MacTrayIconService(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        _application = application;
        var menu = new NativeMenu();
        NativeMenuItem settings = CreateItem(
            "Settings…",
            (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        NativeMenuItem copy = CreateItem(
            "Copy last result",
            (_, _) => CopyLastRequested?.Invoke(this, EventArgs.Empty));
        NativeMenuItem history = CreateItem(
            "Transcript history…",
            (_, _) => HistoryRequested?.Invoke(this, EventArgs.Empty));
        NativeMenuItem permissions = CreateItem(
            "Privacy permissions…",
            (_, _) => PermissionsRequested?.Invoke(this, EventArgs.Empty));
        NativeMenuItem quit = CreateItem(
            "Quit DictaClone",
            (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        menu.Add(settings);
        menu.Add(copy);
        menu.Add(history);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(permissions);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(quit);

        _trayIcon = new TrayIcon
        {
            ToolTipText = "DictaClone",
            Menu = menu,
            IsVisible = true,
        };
        var iconUri = new Uri(
            "avares://DictaClone.Mac.App/Assets/dictaclone.png");
        if (AssetLoader.Exists(iconUri))
        {
            using Stream stream = AssetLoader.Open(iconUri);
            _trayIcon.Icon = new WindowIcon(stream);
        }

        _icons = [_trayIcon];
        TrayIcon.SetIcons(application, _icons);
    }

    public event EventHandler? SettingsRequested;

    public event EventHandler? CopyLastRequested;

    public event EventHandler? HistoryRequested;

    public event EventHandler? PermissionsRequested;

    public event EventHandler? ExitRequested;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trayIcon.IsVisible = false;
        TrayIcon.SetIcons(_application, null);
    }

    private static NativeMenuItem CreateItem(
        string header,
        EventHandler? handler)
    {
        var item = new NativeMenuItem(header);
        if (handler is not null)
        {
            item.Click += handler;
        }

        return item;
    }
}
