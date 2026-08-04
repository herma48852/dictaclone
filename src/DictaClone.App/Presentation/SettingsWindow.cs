using System.Collections;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using DictaClone.Audio;
using DictaClone.Core.Hotkeys;
using DictaClone.Core.Settings;
using DictaClone.Speech;
using DictaClone.Windows.Input;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfDataGrid = System.Windows.Controls.DataGrid;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfLabel = System.Windows.Controls.Label;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPasswordBox = System.Windows.Controls.PasswordBox;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfSlider = System.Windows.Controls.Slider;
using WpfTabControl = System.Windows.Controls.TabControl;
using WpfTextBox = System.Windows.Controls.TextBox;

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
    private readonly WpfComboBox _insertionMode;
    private readonly WpfSlider _characterDelay;
    private readonly TextBlock _characterDelayLabel;
    private readonly ObservableCollection<EditablePair> _vocabularyEntries;
    private readonly ObservableCollection<EditablePair> _expansionEntries;
    private readonly WpfComboBox _workDomain;
    private readonly WpfCheckBox _startWithWindows;
    private readonly WpfCheckBox _historyEnabled;
    private readonly WpfTextBox _historyLimit;
    private readonly TextBlock _knowledgeValidation;
    private readonly TextBlock _preferenceValidation;
    private readonly WpfCheckBox _smartEditEnabled;
    private readonly WpfTextBox _smartEditEndpoint;
    private readonly WpfTextBox _smartEditModel;
    private readonly WpfTextBox _smartEditInstructions;
    private readonly WpfPasswordBox _smartEditApiKey;
    private readonly TextBlock _smartEditValidation;
    private bool _smartEditCredentialStored;
    private ShortcutRecordingSession? _recording;
    private HotkeyChord? _candidateChord;

    public SettingsWindow(
        IEnumerable<HotkeyBinding> bindings,
        AudioSettings? audioSettings = null,
        TranscriptionSettings? transcriptionSettings = null,
        IReadOnlyList<MicrophoneDeviceInfo>? audioDevices = null,
        InsertionSettings? insertionSettings = null,
        TextProcessingSettings? textSettings = null,
        ApplicationPreferences? preferences = null,
        bool firstRun = false,
        SmartEditSettings? smartEditSettings = null,
        bool smartEditCredentialStored = false)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _bindings = [.. bindings];
        Audio = audioSettings ?? DictaCloneSettings.Default.Audio;
        Transcription =
            transcriptionSettings ?? DictaCloneSettings.Default.Transcription;
        Insertion = insertionSettings ?? DictaCloneSettings.Default.Insertion;
        Text = textSettings ?? DictaCloneSettings.Default.Text;
        Preferences = preferences ?? DictaCloneSettings.Default.Preferences;
        SmartEdit = smartEditSettings ?? DictaCloneSettings.Default.SmartEdit;
        _smartEditCredentialStored = smartEditCredentialStored;
        _vocabularyEntries = new(Text.Vocabulary.Select(entry =>
            new EditablePair(entry.SpokenForm, entry.WrittenForm)));
        _expansionEntries = new(Text.Expansions.Select(entry =>
            new EditablePair(entry.Trigger, entry.Replacement)));
        audioDevices ??= [];

        Title = firstRun
            ? "DictaClone first-run setup"
            : "DictaClone settings";
        Width = 820;
        Height = 760;
        MinWidth = 680;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

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
            Text = firstRun
                ? "Choose a microphone and local model, then apply settings to complete setup."
                : "Changes are saved automatically when you apply them.",
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
        sensitivityPanel.Children.Add(_silenceThreshold);
        Grid.SetColumn(_silenceThresholdLabel, 1);
        sensitivityPanel.Children.Add(_silenceThresholdLabel);
        Grid.SetRow(sensitivityPanel, 1);
        Grid.SetColumnSpan(sensitivityPanel, 3);

        _insertionMode = CreateComboBox(Enum.GetValues<TextInsertionMode>());
        _insertionMode.SelectedItem = Insertion.Mode;
        _characterDelay = new()
        {
            Minimum = 0,
            Maximum = 100,
            Value = Insertion.CharacterDelay.TotalMilliseconds,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            Margin = new(0, 4, 12, 0),
        };
        _characterDelayLabel = new()
        {
            Text = FormatDelay(_characterDelay.Value),
            Width = 54,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var delayControl = new Grid();
        delayControl.ColumnDefinitions.Add(new()
        {
            Width = new(1, GridUnitType.Star),
        });
        delayControl.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto,
        });
        delayControl.Children.Add(_characterDelay);
        Grid.SetColumn(_characterDelayLabel, 1);
        delayControl.Children.Add(_characterDelayLabel);

        var insertionPanel = new Grid
        {
            Margin = new(0, 12, 0, 0),
        };
        insertionPanel.ColumnDefinitions.Add(new()
        {
            Width = new(1, GridUnitType.Star),
        });
        insertionPanel.ColumnDefinitions.Add(new()
        {
            Width = new(2, GridUnitType.Star),
        });
        insertionPanel.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto,
        });
        AddLabeledControl(
            insertionPanel,
            "Default insertion",
            _insertionMode,
            column: 0);
        AddLabeledControl(
            insertionPanel,
            "Typing delay",
            delayControl,
            column: 1);
        var applyAudioButton = new WpfButton
        {
            Content = "Apply settings",
            MinWidth = 110,
            Padding = new(10, 6, 10, 6),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        Grid.SetColumn(applyAudioButton, 2);
        insertionPanel.Children.Add(applyAudioButton);
        Grid.SetRow(insertionPanel, 2);
        Grid.SetColumnSpan(insertionPanel, 3);

        audioPanel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        audioPanel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        audioPanel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        audioPanel.Children.Add(sensitivityPanel);
        audioPanel.Children.Add(insertionPanel);
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
        (_workDomain, _knowledgeValidation) = CreateKnowledgeTab(
            out TabItem knowledgeTab);
        (_startWithWindows, _historyEnabled, _historyLimit,
            _preferenceValidation) = CreatePrivacyTab(
                firstRun,
                out TabItem privacyTab);
        (_smartEditEnabled, _smartEditEndpoint, _smartEditModel,
            _smartEditInstructions, _smartEditApiKey,
            _smartEditValidation) = CreateSmartEditTab(
                out TabItem smartEditTab);
        var tabs = new WpfTabControl
        {
            Margin = new(12),
        };
        AutomationProperties.SetName(tabs, "DictaClone settings sections");
        KeyboardNavigation.SetTabNavigation(
            tabs,
            KeyboardNavigationMode.Local);
        tabs.Items.Add(new TabItem
        {
            Header = "General",
            Content = root,
        });
        tabs.Items.Add(knowledgeTab);
        tabs.Items.Add(smartEditTab);
        tabs.Items.Add(privacyTab);
        Content = tabs;

        _action.SelectionChanged += (_, _) => SelectCurrentBinding();
        _recordButton.Click += (_, _) => BeginRecording();
        applyButton.Click += (_, _) => ApplyBinding();
        resetButton.Click += (_, _) => ResetDefaults();
        _silenceThreshold.ValueChanged += (_, _) =>
            _silenceThresholdLabel.Text =
                FormatThreshold(_silenceThreshold.Value);
        _characterDelay.ValueChanged += (_, _) =>
            _characterDelayLabel.Text = FormatDelay(_characterDelay.Value);
        applyAudioButton.Click += (_, _) => ApplyAudioSettings();

        RefreshBindings();
        _action.SelectedItem = HotkeyAction.Dictation;
        _activation.SelectedItem = HotkeyActivation.Hold;
    }

    public event EventHandler<HotkeyBindingsChanged>? BindingsChanged;

    public event EventHandler<AudioSpeechSettingsChangedEventArgs>?
        AudioSpeechSettingsChanged;

    public event EventHandler<TextSettingsChangedEventArgs>?
        TextSettingsChanged;

    public event EventHandler<PreferencesChangedEventArgs>?
        PreferencesChanged;

    public event EventHandler<SmartEditSettingsChangedEventArgs>?
        SmartEditSettingsChanged;

    public event EventHandler<SettingsTransferRequestedEventArgs>?
        SettingsImportRequested;

    public event EventHandler<SettingsTransferRequestedEventArgs>?
        SettingsExportRequested;

    public event EventHandler<SettingsTransferRequestedEventArgs>?
        SupportBundleRequested;

    public event EventHandler? MicrophonePermissionHelpRequested;

    public ImmutableArray<HotkeyBinding> Bindings => [.. _bindings];

    public AudioSettings Audio { get; private set; }

    public TranscriptionSettings Transcription { get; private set; }

    public InsertionSettings Insertion { get; private set; }

    public TextProcessingSettings Text { get; private set; }

    public ApplicationPreferences Preferences { get; private set; }

    public SmartEditSettings SmartEdit { get; private set; }

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
        panel.Children.Add(new WpfLabel
        {
            Content = label,
            Target = control,
            FontWeight = FontWeights.SemiBold,
            Padding = new(0),
        });
        panel.Children.Add(control);
        Grid.SetColumn(panel, column);
        grid.Children.Add(panel);
    }

    private (WpfComboBox Domain, TextBlock Validation) CreateKnowledgeTab(
        out TabItem tab)
    {
        var root = new Grid
        {
            Margin = new(18),
        };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new()
        {
            Height = new(1, GridUnitType.Star),
        });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "Knowledge and text expansion",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
        });

        var domain = CreateComboBox(
            Enum.GetValues<WorkDomainPreset>()
                .Select(value => new WorkDomainOption(
                    value,
                    WorkDomainCatalog.GetDisplayName(value)))
                .ToArray());
        domain.SelectedItem = domain.Items
            .Cast<WorkDomainOption>()
            .First(option => option.Value == Text.WorkDomain);
        AutomationProperties.SetName(domain, "Work domain preset");
        var domainPanel = new StackPanel
        {
            Margin = new(0, 12, 0, 12),
        };
        domainPanel.Children.Add(new WpfLabel
        {
            Content = "Work domain",
            Target = domain,
            FontWeight = FontWeights.SemiBold,
            Padding = new(0),
        });
        domainPanel.Children.Add(domain);
        domainPanel.Children.Add(new TextBlock
        {
            Text = "Presets add local recognition hints; custom entries remain authoritative.",
            Opacity = 0.72,
            Margin = new(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetRow(domainPanel, 1);
        root.Children.Add(domainPanel);

        WpfDataGrid vocabulary = CreatePairGrid(
            "Vocabulary entries",
            "Spoken form",
            "Written form",
            _vocabularyEntries);
        WpfDataGrid expansions = CreatePairGrid(
            "Text expansion entries",
            "Trigger",
            "Replacement",
            _expansionEntries);
        var editors = new Grid();
        editors.ColumnDefinitions.Add(new()
        {
            Width = new(1, GridUnitType.Star),
        });
        editors.ColumnDefinitions.Add(new()
        {
            Width = new(1, GridUnitType.Star),
        });
        editors.Children.Add(CreatePairEditor(
            "Vocabulary",
            "Correct recurring names and technical terms.",
            vocabulary,
            _vocabularyEntries));
        UIElement expansionEditor = CreatePairEditor(
            "Expansions",
            "Replace an exact spoken trigger with reusable text.",
            expansions,
            _expansionEntries);
        Grid.SetColumn(expansionEditor, 1);
        editors.Children.Add(expansionEditor);
        Grid.SetRow(editors, 2);
        root.Children.Add(editors);

        var footer = new StackPanel
        {
            Margin = new(0, 12, 0, 0),
        };
        var apply = CreateActionButton("Apply knowledge");
        apply.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        apply.Click += (_, _) => ApplyKnowledgeSettings();
        footer.Children.Add(apply);
        var validation = new TextBlock
        {
            Margin = new(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        footer.Children.Add(validation);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        tab = new()
        {
            Header = "Knowledge",
            Content = root,
        };
        return (domain, validation);
    }

    private (
        WpfCheckBox StartWithWindows,
        WpfCheckBox HistoryEnabled,
        WpfTextBox HistoryLimit,
        TextBlock Validation) CreatePrivacyTab(
        bool firstRun,
        out TabItem tab)
    {
        var panel = new StackPanel
        {
            Margin = new(18),
        };
        panel.Children.Add(new TextBlock
        {
            Text = firstRun ? "Finish first-run setup" : "Privacy and recovery",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Transcript history is local, text-only, and disabled by default. " +
                "Diagnostics never contain transcript or clipboard text.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new(0, 6, 0, 16),
            Opacity = 0.72,
        });

        var startWithWindows = new WpfCheckBox
        {
            Content = "Start DictaClone when I sign in to Windows",
            IsChecked = Preferences.StartWithWindows,
            Margin = new(0, 4, 0, 8),
        };
        var historyEnabled = new WpfCheckBox
        {
            Content = "Keep local transcript history",
            IsChecked = Preferences.HistoryEnabled,
            Margin = new(0, 4, 0, 8),
        };
        var historyLimit = new WpfTextBox
        {
            Text = Preferences.HistoryLimit.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            Width = 90,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new(0, 4, 0, 12),
        };
        AutomationProperties.SetName(historyLimit, "Maximum history entries");
        panel.Children.Add(startWithWindows);
        panel.Children.Add(historyEnabled);
        panel.Children.Add(new WpfLabel
        {
            Content = "Maximum history entries (1-500)",
            Target = historyLimit,
            FontWeight = FontWeights.SemiBold,
            Padding = new(0),
        });
        panel.Children.Add(historyLimit);

        var apply = CreateActionButton(
            firstRun ? "Complete setup" : "Apply privacy settings");
        apply.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        apply.Click += (_, _) => ApplyPreferences();
        panel.Children.Add(apply);

        var transferButtons = new WrapPanel
        {
            Margin = new(0, 18, 0, 0),
        };
        WpfButton import = CreateActionButton("Import settings…");
        import.Click += (_, _) => RequestSettingsImport();
        WpfButton export = CreateActionButton("Export settings…");
        export.Click += (_, _) => RequestSettingsExport();
        WpfButton bundle = CreateActionButton("Create support bundle…");
        bundle.Click += (_, _) => RequestSupportBundle();
        WpfButton permissions = CreateActionButton("Microphone privacy settings");
        permissions.Click += (_, _) =>
            MicrophonePermissionHelpRequested?.Invoke(this, EventArgs.Empty);
        transferButtons.Children.Add(import);
        transferButtons.Children.Add(export);
        transferButtons.Children.Add(bundle);
        transferButtons.Children.Add(permissions);
        panel.Children.Add(transferButtons);

        var validation = new TextBlock
        {
            Margin = new(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(validation);

        tab = new()
        {
            Header = "Privacy & recovery",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel,
            },
        };
        return (startWithWindows, historyEnabled, historyLimit, validation);
    }

    private (
        WpfCheckBox Enabled,
        WpfTextBox Endpoint,
        WpfTextBox Model,
        WpfTextBox Instructions,
        WpfPasswordBox ApiKey,
        TextBlock Validation) CreateSmartEditTab(out TabItem tab)
    {
        var panel = new StackPanel
        {
            Margin = new(18),
        };
        panel.Children.Add(new TextBlock
        {
            Text = "Smart Edit provider",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "When enabled, DictaClone sends the spoken instruction and " +
                "the explicitly selected text to the HTTPS provider below. " +
                "Microphone audio stays on this computer. The API key is kept " +
                "in Windows Credential Manager and is never exported.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new(0, 6, 0, 16),
            Opacity = 0.78,
        });

        var enabled = new WpfCheckBox
        {
            Content = "Enable cloud Smart Edit and allow selected text to be sent",
            IsChecked = SmartEdit.Enabled,
            Margin = new(0, 4, 0, 12),
        };
        AutomationProperties.SetName(enabled, "Enable cloud Smart Edit");
        panel.Children.Add(enabled);

        var endpoint = CreateTextField(
            panel,
            "Provider HTTPS endpoint",
            "Smart Edit provider HTTPS endpoint",
            SmartEdit.Endpoint);
        var model = CreateTextField(
            panel,
            "Provider model",
            "Smart Edit provider model",
            SmartEdit.Model);
        var instructions = CreateTextField(
            panel,
            "Custom Smart Edit instructions (optional)",
            "Custom Smart Edit instructions",
            SmartEdit.CustomInstructions ?? string.Empty);
        instructions.AcceptsReturn = true;
        instructions.Height = 88;
        instructions.TextWrapping = TextWrapping.Wrap;
        instructions.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        var apiKey = new WpfPasswordBox
        {
            Margin = new(0, 4, 0, 4),
            MinWidth = 300,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(apiKey, "Smart Edit API key");
        panel.Children.Add(new WpfLabel
        {
            Content = "API key (leave blank to keep the stored key)",
            Target = apiKey,
            FontWeight = FontWeights.SemiBold,
            Padding = new(0),
            Margin = new(0, 8, 0, 0),
        });
        panel.Children.Add(apiKey);
        panel.Children.Add(new TextBlock
        {
            Text = _smartEditCredentialStored
                ? "A Smart Edit API key is stored for this Windows account."
                : "No Smart Edit API key is stored.",
            Opacity = 0.72,
            Margin = new(0, 0, 0, 12),
        });

        var buttons = new WrapPanel();
        WpfButton apply = CreateActionButton("Apply Smart Edit settings");
        apply.Click += (_, _) => ApplySmartEditSettings(deleteCredential: false);
        WpfButton remove = CreateActionButton("Remove stored API key");
        remove.Click += (_, _) => ApplySmartEditSettings(deleteCredential: true);
        buttons.Children.Add(apply);
        buttons.Children.Add(remove);
        panel.Children.Add(buttons);

        var validation = new TextBlock
        {
            Margin = new(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(validation);

        tab = new()
        {
            Header = "Smart Edit",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel,
            },
        };
        return (enabled, endpoint, model, instructions, apiKey, validation);
    }

    private static WpfTextBox CreateTextField(
        System.Windows.Controls.Panel panel,
        string label,
        string accessibleName,
        string value)
    {
        var field = new WpfTextBox
        {
            Text = value,
            Margin = new(0, 4, 0, 8),
            MinWidth = 300,
        };
        AutomationProperties.SetName(field, accessibleName);
        panel.Children.Add(new WpfLabel
        {
            Content = label,
            Target = field,
            FontWeight = FontWeights.SemiBold,
            Padding = new(0),
        });
        panel.Children.Add(field);
        return field;
    }

    private static WpfDataGrid CreatePairGrid(
        string accessibleName,
        string firstHeader,
        string secondHeader,
        ObservableCollection<EditablePair> entries)
    {
        var grid = new WpfDataGrid
        {
            ItemsSource = entries,
            AutoGenerateColumns = false,
            CanUserAddRows = true,
            CanUserDeleteRows = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            MinHeight = 230,
            Margin = new(0, 8, 0, 0),
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = firstHeader,
            Binding = new WpfBinding(nameof(EditablePair.First))
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = secondHeader,
            Binding = new WpfBinding(nameof(EditablePair.Second))
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        AutomationProperties.SetName(grid, accessibleName);
        return grid;
    }

    private static StackPanel CreatePairEditor(
        string title,
        string description,
        WpfDataGrid grid,
        ObservableCollection<EditablePair> entries)
    {
        var panel = new StackPanel
        {
            Margin = new(0, 0, 10, 0),
        };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
        });
        panel.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
        });
        panel.Children.Add(grid);
        var buttons = new WrapPanel
        {
            Margin = new(0, 8, 0, 0),
        };
        WpfButton add = CreateActionButton("Add row");
        add.Click += (_, _) =>
        {
            var entry = new EditablePair();
            entries.Add(entry);
            grid.SelectedItem = entry;
            grid.ScrollIntoView(entry);
            _ = grid.BeginEdit();
        };
        WpfButton remove = CreateActionButton("Remove selected");
        remove.Click += (_, _) =>
        {
            if (grid.SelectedItem is EditablePair entry)
            {
                _ = entries.Remove(entry);
            }
        };
        buttons.Children.Add(add);
        buttons.Children.Add(remove);
        panel.Children.Add(buttons);
        return panel;
    }

    private static WpfButton CreateActionButton(string text) => new()
    {
        Content = text,
        MinWidth = 110,
        Padding = new(10, 6, 10, 6),
        Margin = new(0, 0, 8, 0),
    };

    private void ApplyKnowledgeSettings()
    {
        if (_workDomain.SelectedItem is not WorkDomainOption domain)
        {
            _knowledgeValidation.Text = "Choose a work domain.";
            return;
        }

        if (!TryCreatePairs(
                _vocabularyEntries,
                out ImmutableArray<(string First, string Second)> vocabulary) ||
            !TryCreatePairs(
                _expansionEntries,
                out ImmutableArray<(string First, string Second)> expansions))
        {
            _knowledgeValidation.Text =
                "Each knowledge row must contain both fields or be completely blank.";
            return;
        }

        var candidate = Text with
        {
            WorkDomain = domain.Value,
            Vocabulary =
            [
                .. vocabulary.Select(pair =>
                    new VocabularyEntry(pair.First, pair.Second)),
            ],
            Expansions =
            [
                .. expansions.Select(pair =>
                    new TextExpansion(pair.First, pair.Second)),
            ],
        };
        var errors = SettingsValidator.Validate(
            DictaCloneSettings.Default with { Text = candidate });
        SettingsValidationError? error = errors.FirstOrDefault(item =>
            item.Path.StartsWith("Text", StringComparison.Ordinal));
        if (error is not null)
        {
            _knowledgeValidation.Text = error.Message;
            return;
        }

        Text = candidate;
        _knowledgeValidation.Text = "Knowledge settings submitted for saving.";
        TextSettingsChanged?.Invoke(this, new(Text));
    }

    private void ApplyPreferences()
    {
        if (!int.TryParse(
                _historyLimit.Text,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out int historyLimit) ||
            historyLimit is < 1 or > 500)
        {
            _preferenceValidation.Text =
                "History limit must be a whole number from 1 through 500.";
            return;
        }

        Preferences = new(
            FirstRunCompleted: true,
            StartWithWindows: _startWithWindows.IsChecked == true,
            HistoryEnabled: _historyEnabled.IsChecked == true,
            historyLimit);
        _preferenceValidation.Text = "Privacy settings submitted for saving.";
        PreferencesChanged?.Invoke(this, new(Preferences));
    }

    private void ApplySmartEditSettings(bool deleteCredential)
    {
        bool enabled = _smartEditEnabled.IsChecked == true;
        string apiKey = _smartEditApiKey.Password;
        bool willHaveCredential = !deleteCredential &&
            (_smartEditCredentialStored || !string.IsNullOrWhiteSpace(apiKey));
        if (enabled && !willHaveCredential)
        {
            _smartEditValidation.Text =
                "Enter an API key before enabling cloud Smart Edit.";
            return;
        }

        SmartEditSettings candidate = SmartEdit with
        {
            Enabled = enabled && !deleteCredential,
            Endpoint = _smartEditEndpoint.Text.Trim(),
            Model = _smartEditModel.Text.Trim(),
            CustomInstructions = string.IsNullOrWhiteSpace(
                _smartEditInstructions.Text)
                ? null
                : _smartEditInstructions.Text.Trim(),
        };
        SettingsValidationError? error = SettingsValidator.Validate(
            DictaCloneSettings.Default with { SmartEdit = candidate })
            .FirstOrDefault(item => item.Path.StartsWith(
                "SmartEdit",
                StringComparison.Ordinal));
        if (error is not null)
        {
            _smartEditValidation.Text = error.Message;
            return;
        }

        SmartEdit = candidate;
        _smartEditCredentialStored = willHaveCredential;
        _smartEditValidation.Text = deleteCredential
            ? "API-key removal and Smart Edit disable submitted for saving."
            : "Smart Edit settings submitted for saving.";
        SmartEditSettingsChanged?.Invoke(
            this,
            new(candidate, string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
                deleteCredential));
        _smartEditApiKey.Clear();
    }

    private static bool TryCreatePairs(
        IEnumerable<EditablePair> source,
        out ImmutableArray<(string First, string Second)> pairs)
    {
        var builder = ImmutableArray.CreateBuilder<(string, string)>();
        foreach (EditablePair entry in source)
        {
            string first = entry.First?.Trim() ?? string.Empty;
            string second = entry.Second?.Trim() ?? string.Empty;
            if (first.Length == 0 && second.Length == 0)
            {
                continue;
            }

            if (first.Length == 0 || second.Length == 0)
            {
                pairs = [];
                return false;
            }

            builder.Add((first, second));
        }

        pairs = builder.ToImmutable();
        return true;
    }

    private void RequestSettingsImport()
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "Import DictaClone settings",
            Filter = "DictaClone settings (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            SettingsImportRequested?.Invoke(this, new(dialog.FileName));
        }
    }

    private void RequestSettingsExport()
    {
        var dialog = new WpfSaveFileDialog
        {
            Title = "Export DictaClone settings",
            Filter = "DictaClone settings (*.json)|*.json",
            FileName = "dictaclone-settings.json",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            SettingsExportRequested?.Invoke(this, new(dialog.FileName));
        }
    }

    private void RequestSupportBundle()
    {
        var dialog = new WpfSaveFileDialog
        {
            Title = "Create privacy-safe support bundle",
            Filter = "ZIP archive (*.zip)|*.zip",
            FileName = "dictaclone-support.zip",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            SupportBundleRequested?.Invoke(this, new(dialog.FileName));
        }
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
            Enabled: action != HotkeyAction.SmartEdit || SmartEdit.Enabled,
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
        _validation.Text = "Binding submitted for saving.";
        BindingsChanged?.Invoke(this, new([.. _bindings]));
    }

    private void ResetDefaults()
    {
        _bindings.Clear();
        _bindings.AddRange(HotkeyDefaults.Bindings);
        RefreshBindings();
        SelectCurrentBinding();
        _validation.Text = "Default bindings submitted for saving.";
        BindingsChanged?.Invoke(this, new([.. _bindings]));
    }

    private void ApplyAudioSettings()
    {
        if (_audioDevice.SelectedItem is not AudioDeviceOption device ||
            _model.SelectedItem is not string model ||
            _language.SelectedItem is not string language ||
            _insertionMode.SelectedItem is not TextInsertionMode insertionMode)
        {
            _validation.Text =
                "Choose a microphone, model, language, and insertion mode.";
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
        Insertion = new(
            insertionMode,
            TimeSpan.FromMilliseconds(_characterDelay.Value));
        _validation.Text =
            "Audio, transcription, and insertion settings submitted for saving.";
        AudioSpeechSettingsChanged?.Invoke(
            this,
            new(Audio, Transcription, Insertion));
    }

    private void RefreshBindings()
    {
        _bindingList.ItemsSource = _bindings
            .OrderBy(binding => binding.Action)
            .Select(binding =>
                $"{binding.Action,-12}  {binding.Chord,-24}  " +
                $"{binding.Activation,-8}  " +
                $"{(binding.Enabled ? "Enabled" : "Disabled")}")
            .ToArray();
    }

    private static string FormatThreshold(double value) =>
        value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatDelay(double value) =>
        $"{value:0} ms";

    private sealed record AudioDeviceOption(string? Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record WorkDomainOption(
        WorkDomainPreset Value,
        string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed class EditablePair
    {
        public EditablePair()
            : this(string.Empty, string.Empty)
        {
        }

        public EditablePair(string first, string second)
        {
            First = first;
            Second = second;
        }

        public string? First { get; set; }

        public string? Second { get; set; }
    }
}

public sealed record HotkeyBindingsChanged(
    ImmutableArray<HotkeyBinding> Bindings);

public sealed class AudioSpeechSettingsChangedEventArgs(
    AudioSettings audio,
    TranscriptionSettings transcription,
    InsertionSettings insertion) : EventArgs
{
    public AudioSettings Audio { get; } = audio;

    public TranscriptionSettings Transcription { get; } = transcription;

    public InsertionSettings Insertion { get; } = insertion;
}

public sealed class TextSettingsChangedEventArgs(
    TextProcessingSettings settings) : EventArgs
{
    public TextProcessingSettings Settings { get; } = settings;
}

public sealed class PreferencesChangedEventArgs(
    ApplicationPreferences preferences) : EventArgs
{
    public ApplicationPreferences Preferences { get; } = preferences;
}

public sealed class SmartEditSettingsChangedEventArgs(
    SmartEditSettings settings,
    string? apiKey,
    bool deleteCredential) : EventArgs
{
    public SmartEditSettings Settings { get; } = settings;

    public string? ApiKey { get; } = apiKey;

    public bool DeleteCredential { get; } = deleteCredential;
}

public sealed class SettingsTransferRequestedEventArgs(string path) : EventArgs
{
    public string Path { get; } = path;
}
