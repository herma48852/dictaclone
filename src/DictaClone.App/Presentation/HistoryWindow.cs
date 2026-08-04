using System.Collections.Immutable;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DictaClone.Core.Contracts;
using WpfButton = System.Windows.Controls.Button;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace DictaClone.App.Presentation;

public sealed class HistoryWindow : Window
{
    private readonly WpfListBox _entries;

    public HistoryWindow(ImmutableArray<TranscriptHistoryEntry> entries)
    {
        Title = "DictaClone transcript history";
        Width = 680;
        Height = 480;
        MinWidth = 520;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        var root = new Grid
        {
            Margin = new(18),
        };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new()
        {
            Height = new(1, GridUnitType.Star),
        });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(new TextBlock
        {
            Text = "Local transcript history",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
        });

        _entries = new()
        {
            Margin = new(0, 12, 0, 12),
        };
        AutomationProperties.SetName(_entries, "Saved transcripts");
        Grid.SetRow(_entries, 1);
        root.Children.Add(_entries);

        var buttons = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
        };
        var copy = new WpfButton
        {
            Content = "Copy selected",
            Padding = new(10, 6, 10, 6),
            MinWidth = 120,
        };
        copy.Click += (_, _) =>
        {
            if (_entries.SelectedItem is HistoryDisplayEntry selected)
            {
                CopyRequested?.Invoke(this, new(selected.Entry));
            }
        };
        var clear = new WpfButton
        {
            Content = "Clear history",
            Padding = new(10, 6, 10, 6),
            MinWidth = 120,
            Margin = new(8, 0, 0, 0),
        };
        clear.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);
        buttons.Children.Add(copy);
        buttons.Children.Add(clear);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;

        ReplaceEntries(entries);
    }

    public event EventHandler<HistoryCopyRequestedEventArgs>? CopyRequested;

    public event EventHandler? ClearRequested;

    public int EntryCount => _entries.Items.Count;

    public void ReplaceEntries(ImmutableArray<TranscriptHistoryEntry> entries)
    {
        _entries.ItemsSource = entries
            .OrderByDescending(entry => entry.CreatedUtc)
            .Select(entry => new HistoryDisplayEntry(entry))
            .ToArray();
    }

    private sealed record HistoryDisplayEntry(TranscriptHistoryEntry Entry)
    {
        public override string ToString()
        {
            string preview = Entry.Text.ReplaceLineEndings(" ");
            if (preview.Length > 160)
            {
                preview = preview[..159] + "…";
            }

            return $"{Entry.CreatedUtc.ToLocalTime():g}  {preview}";
        }
    }
}

public sealed class HistoryCopyRequestedEventArgs(
    TranscriptHistoryEntry entry) : EventArgs
{
    public TranscriptHistoryEntry Entry { get; } = entry;
}
