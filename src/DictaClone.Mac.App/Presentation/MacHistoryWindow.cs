using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using DictaClone.Core.Contracts;

namespace DictaClone.Mac.Presentation;

public sealed class MacHistoryWindow : Window
{
    private readonly ListBox _entries = new();
    private bool _allowClose;

    public MacHistoryWindow()
    {
        Title = "DictaClone Transcript History";
        Width = 680;
        Height = 480;
        MinWidth = 520;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var copy = new Button { Content = "Copy selected" };
        copy.Click += (_, _) =>
        {
            if (_entries.SelectedItem is HistoryDisplayEntry entry)
            {
                CopyRequested?.Invoke(this, new(entry.Entry.Text));
            }
        };
        var clear = new Button { Content = "Clear history" };
        clear.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Hide();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { copy, clear, close },
        };
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(14),
        };
        root.Children.Add(_entries);
        Grid.SetRow(buttons, 1);
        buttons.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(buttons);
        Content = root;
        Closing += (_, eventArgs) =>
        {
            if (!_allowClose)
            {
                eventArgs.Cancel = true;
                Hide();
            }
        };
    }

    public event EventHandler<MacTextEventArgs>? CopyRequested;

    public event EventHandler? ClearRequested;

    public void SetEntries(IReadOnlyList<TranscriptHistoryEntry> entries)
    {
        _entries.ItemsSource = entries
            .OrderByDescending(entry => entry.CreatedUtc)
            .Select(entry => new HistoryDisplayEntry(entry))
            .ToArray();
    }

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    private sealed class HistoryDisplayEntry(TranscriptHistoryEntry entry)
    {
        public TranscriptHistoryEntry Entry { get; } = entry;

        public override string ToString() =>
            $"{Entry.CreatedUtc.ToLocalTime():g}  —  {Entry.Text}";
    }
}

public sealed class MacTextEventArgs(string text) : EventArgs
{
    public string Text { get; } = text;
}
