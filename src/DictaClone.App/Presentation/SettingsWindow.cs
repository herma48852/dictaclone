using System.Collections;
using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DictaClone.Audio;
using DictaClone.Core.Hotkeys;
using DictaClone.Core.Settings;
using DictaClone.Speech;
using DictaClone.Windows.Input;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfSlider = System.Windows.Controls.Slider;

namespace DictaClone.App.Presentation;

public sealed class SettingsWindow : Window
{
    private static readonly string[] SupportedLanguages = ["en", "auto"];

    private readonly List<HotkeyBinding> _bindings;
    private readonly WpfListBox _bindingList;
    private readonly WpfComboBox _action;
    private readonly WpfComboBox _activation;
    private readonly TextBlock _recordedChord;
    private readonly TextBlock _validation;
    private readonly WpfButton _recordButton;
    private readonly WpfComboBox _audioDevice;
    private readonly WpfComboBox _model;
    private readonly WpfComboBox _language;
    private readonly WpfSlider _silenceThreshold;
    private readonly TextBlock _silenceThresholdLabel;
    private ShortcutRecordingSession? _recording;
    private HotkeyChord? _candidateChord;

    public SettingsWindow(
        IEnumerable<HotkeyBinding> bindings,
        AudioSettings? audioSettings = null,
        TranscriptionSettings? transcriptionSettings = null,
        IReadOnlyList<MicrophoneDeviceInfo>? audioDevices = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _bindings = [.. bindings];
        Audio = audioSettings ?? DictaCloneSettings.Default.Audio;
        Transcription =
            transcriptionSettings ?? DictaCloneSettings.Default.Transcription;
        audioDevices ??= [];

        Title = "DictaClone settings";
        Width = 680;
        Height = 650;
        MinWidth = 600;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new Grid
        {
            Margin = new(24),
        };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Keyboard, mouse, and foot-pedal shortcuts",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Changes apply for this run. Persistent settings arrive in Milestone 5.",
            Margin = new(0, 6, 0, 18),
            Opacity = 0.72,
        });
        root.Children.Add(heading);

        var audioPanel = new Grid
        {
            Margin = new(0, 0, 0, 18),
        };
        for (int column = 0; column < 3; column++)
        {
            audioPanel.ColumnDefinitions.Add(new()
            {
                Width = new(1, GridUnitType.Star),
            });
        }

        AudioDeviceOption[] deviceOptions =
        [
            new(null, "System default (follow changes)"),
            .. audioDevices.Select(device => new AudioDeviceOption(
                device.Id,
                device.IsDefault
                    ? $"{device.FriendlyName} (current default)"
                    : device.FriendlyName)),
        ];
        _audioDevice = CreateComboBox(deviceOptions);
        _audioDevice.SelectedItem = deviceOptions.FirstOrDefault(option =>
            string.Equals(
                option.Id,
                Audio.DeviceId,
                StringComparison.Ordinal));
        _audioDevice.SelectedIndex = Math.Max(_audioDevice.SelectedIndex, 0);

        _model = CreateComboBox(
            WhisperModelCatalog.AvailableModels
                .OrderBy(model => model.Length)
                .Select(model => model.Name)
                .ToArray());
        _model.SelectedItem = Transcription.Model;
        _model.SelectedIndex = Math.Max(_model.SelectedIndex, 0);

        _language = CreateComboBox(SupportedLanguages);
        _language.SelectedItem = Transcription.Language;
        _language.SelectedIndex = Math.Max(_language.SelectedIndex, 0);

        AddLabeledControl(audioPanel, "Microphone", _audioDevice, column: 0);
        AddLabeledControl(audioPanel, "Local model", _model, column: 1);
        AddLabeledControl(audioPanel, "Language", _language, column: 2);

        var sensitivityPanel = new Grid
        {
            Margin = new(0, 10, 0, 0),
        };
        sensitivityPanel.ColumnDefinitions.Add(new()
        {
            Width = new(1, GridUnitType.Star),
        });
        sensitivityPanel.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto,
        });
        sensitivityPanel.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto,
        });
        _silenceThreshold = new()
        {
            Minimum = 0,
            Maximum = 0.1,
            Value = Audio.SilenceThreshold,
            TickFrequency = 0.005,
            IsSnapToTickEnabled = true,
            Margin = new(0, 0, 12, 0),
        };
        _silenceThresholdLabel = new()
        {
            Text = FormatThreshold(_silenceThreshold.Value),
            Width = 54,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var applyAudioButton = new WpfButton
        {
            Content = "Apply audio",
            MinWidth = 110,
            Padding = new(10, 6, 10, 6),
        };
        sensitivityPanel.Children.Add(_silenceThreshold);
        Grid.SetColumn(_silenceThresholdLabel, 1);
        sensitivityPanel.Children.Add(_silenceThresholdLabel);
        Grid.SetColumn(applyAudioButton, 2);
        sensitivityPanel.Children.Add(applyAudioButton);
        Grid.SetRow(sensitivityPanel, 1);
        Grid.SetColumnSpan(sensitivityPanel, 3);
        audioPanel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        audioPanel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        audioPanel.Children.Add(sensitivityPanel);
        Grid.SetRow(audioPanel, 1);
        root.Children.Add(audioPanel);

        _bindingList = new()
        {
            Margin = new(0, 0, 0, 18),
        };
        Grid.SetRow(_bindingList, 2);
        root.Children.Add(_bindingList);

        var editor = new Grid();
        editor.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        editor.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        editor.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        editor.RowDefinitions.Add(new() { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new() { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new() { Height = GridLength.Auto });

        _action = CreateComboBox(Enum.GetValues<HotkeyAction>());
        _activation = CreateComboBox(Enum.GetValues<HotkeyActivation>());
        _recordedChord = new()
        {
            Text = "(choose an action)",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new(8),
        };
        AddLabeledControl(editor, "Action", _action, column: 0);
        AddLabeledControl(editor, "Behavior", _activation, column: 1);
        AddLabeledControl(editor, "Shortcut", _recordedChord, column: 2);

        var buttons = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            Margin = new(0, 12, 0, 0),
        };
        _recordButton = new()
        {
            Content = "Record shortcut",
            MinWidth = 130,
            Padding = new(10, 6, 10, 6),
        };
        var applyButton = new WpfButton
        {
            Content = "Apply binding",
            MinWidth = 120,
            Padding = new(10, 6, 10, 6),
            Margin = new(8, 0, 0, 0),
        };
        var resetButton = new WpfButton
        {
            Content = "Reset defaults",
            MinWidth = 120,
            Padding = new(10, 6, 10, 6),
            Margin = new(8, 0, 0, 0),
        };
        buttons.Children.Add(_recordButton);
        buttons.Children.Add(applyButton);
        buttons.Children.Add(resetButton);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 3);
        editor.Children.Add(buttons);

        _validation = new()
        {
            Margin = new(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(_validation, 2);
        Grid.SetColumnSpan(_validation, 3);
        editor.Children.Add(_validation);

        Grid.SetRow(editor, 3);
        root.Children.Add(editor);
        Content = root;

        _action.SelectionChanged += (_, _) => SelectCurrentBinding();
        _recordButton.Click += (_, _) => BeginRecording();
        applyButton.Click += (_, _) => ApplyBinding();
        resetButton.Click += (_, _) => ResetDefaults();
        _silenceThreshold.ValueChanged += (_, _) =>
            _silenceThresholdLabel.Text =
                FormatThreshold(_silenceThreshold.Value);
        applyAudioButton.Click += (_, _) => ApplyAudioSettings();

        RefreshBindings();
        _action.SelectedItem = HotkeyAction.Dictation;
        _activation.SelectedItem = HotkeyActivation.Hold;
    }

    public event EventHandler<HotkeyBindingsChanged>? BindingsChanged;

    public event EventHandler<AudioSpeechSettingsChangedEventArgs>?
        AudioSpeechSettingsChanged;

    public ImmutableArray<HotkeyBinding> Bindings => [.. _bindings];

    public AudioSettings Audio { get; private set; }

    public TranscriptionSettings Transcription { get; private set; }

    protected override void OnPreviewKeyDown(WpfKeyEventArgs e)
    {
        if (_recording is not null)
        {
            Key key = e.Key == Key.System
                ? e.SystemKey
                : e.Key;
            ProcessRecordingKey(key, isPressed: true);
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewKeyUp(WpfKeyEventArgs e)
    {
        if (_recording is not null)
        {
            Key key = e.Key == Key.System
                ? e.SystemKey
                : e.Key;
            ProcessRecordingKey(key, isPressed: false);
            e.Handled = true;
        }

        base.OnPreviewKeyUp(e);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (_recording is not null &&
            WpfInputMapper.TryMapMouse(
                e.ChangedButton,
                out RawInputControl control))
        {
            CompleteIfReady(_recording.Process(new(control, IsPressed: true)));
            e.Handled = true;
        }

        base.OnPreviewMouseDown(e);
    }

    private static WpfComboBox CreateComboBox(IEnumerable values) =>
        new()
        {
            ItemsSource = values,
            Margin = new(0, 4, 8, 0),
            MinWidth = 130,
        };

    private static void AddLabeledControl(
        Grid grid,
        string label,
        UIElement control,
        int column)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
        });
        panel.Children.Add(control);
        Grid.SetColumn(panel, column);
        grid.Children.Add(panel);
    }

    private void SelectCurrentBinding()
    {
        if (_action.SelectedItem is not HotkeyAction action)
        {
            return;
        }

        HotkeyBinding? current = _bindings.Find(binding => binding.Action == action);
        _candidateChord = current?.Chord;
        _recordedChord.Text = _candidateChord?.ToString() ?? "(not assigned)";
        _activation.SelectedItem =
            current?.Activation ?? HotkeyActivation.Hold;
        _validation.Text = string.Empty;
    }

    private void BeginRecording()
    {
        _recording = new();
        _candidateChord = null;
        _recordedChord.Text = "Press a shortcut…";
        _recordButton.Content = "Listening…";
        _validation.Text =
            "Modifier-only shortcuts complete when the final modifier is released.";
        _recordButton.Focus();
    }

    private void ProcessRecordingKey(Key key, bool isPressed)
    {
        if (_recording is null ||
            !WpfInputMapper.TryMapKey(key, out RawInputControl control))
        {
            return;
        }

        CompleteIfReady(_recording.Process(new(control, isPressed)));
    }

    private void CompleteIfReady(HotkeyChord? chord)
    {
        if (!chord.HasValue)
        {
            return;
        }

        _candidateChord = chord;
        _recording = null;
        _recordedChord.Text = chord.Value.ToString();
        _recordButton.Content = "Record shortcut";
        _validation.Text = "Shortcut captured. Choose Apply binding to use it.";
    }

    private void ApplyBinding()
    {
        if (_action.SelectedItem is not HotkeyAction action ||
            _activation.SelectedItem is not HotkeyActivation activation ||
            !_candidateChord.HasValue)
        {
            _validation.Text = "Choose an action and record a valid shortcut.";
            return;
        }

        var candidate = new HotkeyBinding(
            action,
            _candidateChord.Value,
            Enabled: true,
            activation);
        List<HotkeyBinding> updated =
        [
            .. _bindings.Where(binding => binding.Action != action),
            candidate,
        ];
        var conflicts = HotkeyConflictDetector.Find(updated);
        if (conflicts.Length > 0)
        {
            _validation.Text =
                $"Conflict: {conflicts[0].First} and " +
                $"{conflicts[0].Second} both use {conflicts[0].Chord}.";
            return;
        }

        _bindings.Clear();
        _bindings.AddRange(updated.OrderBy(binding => binding.Action));
        RefreshBindings();
        _validation.Text = "Binding applied for this run.";
        BindingsChanged?.Invoke(this, new([.. _bindings]));
    }

    private void ResetDefaults()
    {
        _bindings.Clear();
        _bindings.AddRange(HotkeyDefaults.Bindings);
        RefreshBindings();
        SelectCurrentBinding();
        _validation.Text = "Default bindings restored for this run.";
        BindingsChanged?.Invoke(this, new([.. _bindings]));
    }

    private void ApplyAudioSettings()
    {
        if (_audioDevice.SelectedItem is not AudioDeviceOption device ||
            _model.SelectedItem is not string model ||
            _language.SelectedItem is not string language)
        {
            _validation.Text = "Choose a microphone, model, and language.";
            return;
        }

        Audio = Audio with
        {
            DeviceId = device.Id,
            SilenceThreshold = _silenceThreshold.Value,
        };
        Transcription = Transcription with
        {
            Model = model,
            Language = language,
        };
        _validation.Text =
            "Audio and local transcription settings applied for this run.";
        AudioSpeechSettingsChanged?.Invoke(
            this,
            new(Audio, Transcription));
    }

    private void RefreshBindings()
    {
        _bindingList.ItemsSource = _bindings
            .OrderBy(binding => binding.Action)
            .Select(binding =>
                $"{binding.Action,-12}  {binding.Chord,-24}  " +
                $"{binding.Activation}")
            .ToArray();
    }

    private static string FormatThreshold(double value) =>
        value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);

    private sealed record AudioDeviceOption(string? Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}

public sealed record HotkeyBindingsChanged(
    ImmutableArray<HotkeyBinding> Bindings);

public sealed class AudioSpeechSettingsChangedEventArgs(
    AudioSettings audio,
    TranscriptionSettings transcription) : EventArgs
{
    public AudioSettings Audio { get; } = audio;

    public TranscriptionSettings Transcription { get; } = transcription;
}
