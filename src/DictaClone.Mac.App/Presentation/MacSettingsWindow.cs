using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DictaClone.Core.Hotkeys;
using DictaClone.Core.Settings;
using DictaClone.Mac.Audio;
using DictaClone.Mac.Permissions;

namespace DictaClone.Mac.Presentation;

public sealed class MacSettingsWindow : Window
{
    private readonly ComboBox _microphone = new();
    private readonly ComboBox _model = new();
    private readonly ComboBox _language = new();
    private readonly ComboBox _insertionMode = new();
    private readonly NumericUpDown _typingDelay = new();
    private readonly CheckBox _corrections = new();
    private readonly ComboBox _workDomain = new();
    private readonly TextBox _vocabulary = new();
    private readonly TextBox _expansions = new();
    private readonly CheckBox _startAtLogin = new();
    private readonly CheckBox _history = new();
    private readonly NumericUpDown _historyLimit = new();
    private readonly CheckBox _smartEnabled = new();
    private readonly TextBox _smartEndpoint = new();
    private readonly TextBox _smartModel = new();
    private readonly NumericUpDown _smartTimeout = new();
    private readonly NumericUpDown _smartRetries = new();
    private readonly TextBox _smartInstructions = new();
    private readonly TextBox _apiKey = new();
    private readonly TextBlock _apiKeyState = new();
    private readonly TextBlock _permissionState = new();
    private readonly TextBlock _validation = new();
    private readonly Dictionary<HotkeyAction, TextBox> _hotkeys = [];
    private bool _allowClose;
    private DictaCloneSettings _currentSettings;

    public MacSettingsWindow(
        DictaCloneSettings settings,
        bool apiKeyStored,
        MacPermissionSnapshot permissions,
        IReadOnlyList<MacMicrophoneDevice> microphones)
    {
        _currentSettings = settings;
        Title = "DictaClone Settings";
        Width = 760;
        Height = 680;
        MinWidth = 640;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _microphone.ItemsSource = microphones;
        _model.ItemsSource = new[] { "base.en", "small.en" };
        _language.ItemsSource = new[] { "en", "auto" };
        _insertionMode.ItemsSource = Enum.GetValues<TextInsertionMode>();
        _workDomain.ItemsSource = Enum.GetValues<WorkDomainPreset>();
        _typingDelay.Minimum = 0;
        _typingDelay.Maximum = 100;
        _typingDelay.Increment = 1;
        _historyLimit.Minimum = 1;
        _historyLimit.Maximum = 1000;
        _historyLimit.Increment = 1;
        _smartTimeout.Minimum = 5;
        _smartTimeout.Maximum = 120;
        _smartTimeout.Increment = 1;
        _smartRetries.Minimum = 0;
        _smartRetries.Maximum = 3;
        _smartRetries.Increment = 1;
        _vocabulary.AcceptsReturn = true;
        _vocabulary.MinHeight = 140;
        _vocabulary.PlaceholderText = "spoken form => WrittenForm";
        _expansions.AcceptsReturn = true;
        _expansions.MinHeight = 140;
        _expansions.PlaceholderText = "trigger => replacement text";
        _smartInstructions.AcceptsReturn = true;
        _smartInstructions.MinHeight = 90;
        _apiKey.PasswordChar = '•';
        _apiKey.PlaceholderText = "Leave blank to keep the stored Keychain value";
        _validation.Foreground = Brushes.Firebrick;
        _validation.TextWrapping = TextWrapping.Wrap;

        foreach (HotkeyAction action in Enum.GetValues<HotkeyAction>())
        {
            _hotkeys[action] = new TextBox
            {
                PlaceholderText = "Control+Shift+Space",
            };
        }

        var tabs = new TabControl
        {
            ItemsSource = new[]
            {
                CreateTab("General", BuildGeneralTab()),
                CreateTab("Text & knowledge", BuildTextTab()),
                CreateTab("Privacy & recovery", BuildPrivacyTab()),
                CreateTab("Smart Edit", BuildSmartEditTab()),
            },
        };
        var apply = new Button
        {
            Content = "Apply settings",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        apply.Click += (_, _) => Submit(completeFirstRun: false);
        var complete = new Button
        {
            Content = "Complete setup",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        complete.Click += (_, _) => Submit(completeFirstRun: true);
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Hide();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Children = { close, apply, complete },
        };
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
            Margin = new Thickness(18),
        };
        root.Children.Add(tabs);
        Grid.SetRow(_validation, 1);
        _validation.Margin = new Thickness(4, 10, 4, 4);
        root.Children.Add(_validation);
        Grid.SetRow(buttons, 2);
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
        Update(settings, apiKeyStored, permissions);
    }

    public event EventHandler<MacSettingsApplyEventArgs>? ApplyRequested;

    public event EventHandler<MacPermissionRequestEventArgs>?
        PermissionSettingsRequested;

    public event EventHandler? OpenDataFolderRequested;

    public event EventHandler? ExportSettingsRequested;

    public event EventHandler? ImportSettingsRequested;

    public event EventHandler? SupportBundleRequested;

    public void Update(
        DictaCloneSettings settings,
        bool apiKeyStored,
        MacPermissionSnapshot permissions)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _currentSettings = settings;
        if (_microphone.ItemsSource is IEnumerable<MacMicrophoneDevice> devices)
        {
            _microphone.SelectedItem = devices.FirstOrDefault(device =>
                string.Equals(
                    device.Id,
                    settings.Audio.DeviceId,
                    StringComparison.Ordinal));
        }

        _model.SelectedItem = settings.Transcription.Model;
        _language.SelectedItem = settings.Transcription.Language;
        _insertionMode.SelectedItem = settings.Insertion.Mode;
        _typingDelay.Value = (decimal)settings.Insertion.CharacterDelay.TotalMilliseconds;
        _corrections.IsChecked = settings.Text.EnableCorrections;
        _workDomain.SelectedItem = settings.Text.WorkDomain;
        _vocabulary.Text = string.Join(
            Environment.NewLine,
            settings.Text.Vocabulary.Select(
                item => $"{item.SpokenForm} => {item.WrittenForm}"));
        _expansions.Text = string.Join(
            Environment.NewLine,
            settings.Text.Expansions.Select(
                item => $"{item.Trigger} => {item.Replacement}"));
        _startAtLogin.IsChecked = settings.Preferences.StartWithWindows;
        _history.IsChecked = settings.Preferences.HistoryEnabled;
        _historyLimit.Value = settings.Preferences.HistoryLimit;
        _smartEnabled.IsChecked = settings.SmartEdit.Enabled;
        _smartEndpoint.Text = settings.SmartEdit.Endpoint;
        _smartModel.Text = settings.SmartEdit.Model;
        _smartTimeout.Value = (decimal)settings.SmartEdit.RequestTimeout.TotalSeconds;
        _smartRetries.Value = settings.SmartEdit.MaximumRetries;
        _smartInstructions.Text = settings.SmartEdit.CustomInstructions;
        _apiKey.Text = string.Empty;
        _apiKeyState.Text = apiKeyStored
            ? "An API key is stored in macOS Keychain."
            : "No API key is stored.";

        foreach (HotkeyAction action in Enum.GetValues<HotkeyAction>())
        {
            HotkeyBinding? binding = settings.Hotkeys.FirstOrDefault(
                item => item.Action == action);
            _hotkeys[action].Text = binding is null
                ? string.Empty
                : HotkeyTextCodec.Format(binding.Chord);
        }

        UpdatePermissions(permissions);
        _validation.Text = string.Empty;
    }

    public void UpdatePermissions(MacPermissionSnapshot permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        _permissionState.Text =
            $"Microphone: {permissions.Microphone}{Environment.NewLine}" +
            $"Accessibility: {permissions.Accessibility}{Environment.NewLine}" +
            $"Input Monitoring: {permissions.InputMonitoring} " +
            "(optional when Accessibility is authorized)";
    }

    public void ShowValidation(string message) => _validation.Text = message;

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    private ScrollViewer BuildGeneralTab()
    {
        var panel = CreateForm();
        AddHeading(panel, "Speech and insertion");
        AddField(panel, "Microphone", _microphone);
        AddField(panel, "Local model", _model);
        AddField(panel, "Language", _language);
        AddField(panel, "Insertion mode", _insertionMode);
        AddField(panel, "Typing delay (ms)", _typingDelay);
        AddHeading(panel, "Global shortcuts");
        foreach (HotkeyAction action in Enum.GetValues<HotkeyAction>())
        {
            AddField(panel, GetActionLabel(action), _hotkeys[action]);
        }

        panel.Children.Add(new TextBlock
        {
            Text = "Use Command, Control, Option, Shift, Space, Escape, A–Z, F1–F20, or VolumeDown. VolumeDown is the dedicated speaker-volume key and does not require Fn. Command is stored in the existing cross-platform ‘Windows’ modifier bit for settings compatibility.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        });
        return Wrap(panel);
    }

    private ScrollViewer BuildTextTab()
    {
        var panel = CreateForm();
        panel.Children.Add(_corrections);
        _corrections.Content = "Enable conservative spoken corrections";
        AddField(panel, "Work domain", _workDomain);
        AddHeading(panel, "Custom vocabulary");
        panel.Children.Add(_vocabulary);
        AddHeading(panel, "Text expansions");
        panel.Children.Add(_expansions);
        return Wrap(panel);
    }

    private ScrollViewer BuildPrivacyTab()
    {
        var panel = CreateForm();
        _startAtLogin.Content = "Start DictaClone when I sign in to this Mac";
        _history.Content = "Keep local transcript history";
        panel.Children.Add(_startAtLogin);
        panel.Children.Add(_history);
        AddField(panel, "History limit", _historyLimit);
        AddHeading(panel, "macOS privacy permissions");
        _permissionState.FontWeight = FontWeight.SemiBold;
        _permissionState.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_permissionState);
        var permissions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
        };
        permissions.Children.Add(CreatePermissionButton("Microphone", "microphone"));
        permissions.Children.Add(CreatePermissionButton("Accessibility", "accessibility"));
        permissions.Children.Add(CreatePermissionButton("Input Monitoring", "input"));
        panel.Children.Add(permissions);
        AddHeading(panel, "Recovery and diagnostics");
        panel.Children.Add(CreateActionButtons());
        return Wrap(panel);
    }

    private ScrollViewer BuildSmartEditTab()
    {
        var panel = CreateForm();
        _smartEnabled.Content = "Enable optional cloud Smart Edit";
        panel.Children.Add(_smartEnabled);
        panel.Children.Add(new TextBlock
        {
            Text = "Ordinary dictation stays local. Smart Edit sends transcript text and, when present, selected text to the configured provider.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
        });
        AddField(panel, "Endpoint", _smartEndpoint);
        AddField(panel, "Model", _smartModel);
        AddField(panel, "Timeout (seconds)", _smartTimeout);
        AddField(panel, "Maximum retries", _smartRetries);
        AddHeading(panel, "Custom instructions");
        panel.Children.Add(_smartInstructions);
        AddHeading(panel, "Provider API key");
        panel.Children.Add(_apiKeyState);
        panel.Children.Add(_apiKey);
        return Wrap(panel);
    }

    private WrapPanel CreateActionButtons()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(CreateEventButton(
            "Open data folder",
            () => OpenDataFolderRequested?.Invoke(this, EventArgs.Empty)));
        panel.Children.Add(CreateEventButton(
            "Export settings…",
            () => ExportSettingsRequested?.Invoke(this, EventArgs.Empty)));
        panel.Children.Add(CreateEventButton(
            "Import settings…",
            () => ImportSettingsRequested?.Invoke(this, EventArgs.Empty)));
        panel.Children.Add(CreateEventButton(
            "Create support bundle…",
            () => SupportBundleRequested?.Invoke(this, EventArgs.Empty)));
        return panel;
    }

    private Button CreatePermissionButton(string label, string permission)
    {
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(0, 0, 8, 8),
        };
        button.Click += (_, _) =>
            PermissionSettingsRequested?.Invoke(this, new(permission));
        return button;
    }

    private static Button CreateEventButton(string label, Action action)
    {
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(0, 0, 8, 8),
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void Submit(bool completeFirstRun)
    {
        try
        {
            DictaCloneSettings settings = BuildSettings(completeFirstRun);
            ImmutableArray<SettingsValidationError> errors =
                SettingsValidator.Validate(settings);
            if (!errors.IsEmpty)
            {
                throw new FormatException(
                    $"{errors[0].Path}: {errors[0].Message}");
            }

            _validation.Text = string.Empty;
            ApplyRequested?.Invoke(
                this,
                new(settings, NullIfWhiteSpace(_apiKey.Text), completeFirstRun));
        }
        catch (Exception exception)
            when (exception is FormatException or ArgumentException)
        {
            _validation.Text = exception.Message;
        }
    }

    private DictaCloneSettings BuildSettings(bool completeFirstRun)
    {
        ImmutableArray<HotkeyBinding> hotkeys = Enum
            .GetValues<HotkeyAction>()
            .Select(action =>
            {
                if (!HotkeyTextCodec.TryParse(
                        _hotkeys[action].Text ?? string.Empty,
                        out HotkeyChord chord))
                {
                    throw new FormatException(
                        $"The {GetActionLabel(action)} shortcut is invalid.");
                }

                HotkeyBinding? previous = _currentSettings.Hotkeys
                    .FirstOrDefault(item => item.Action == action);
                bool enabled = action != HotkeyAction.SmartEdit ||
                    _smartEnabled.IsChecked == true;
                return new HotkeyBinding(
                    action,
                    chord,
                    enabled,
                    previous?.Activation ?? HotkeyActivation.Hold);
            })
            .ToImmutableArray();
        var conflicts = HotkeyConflictDetector.Find(hotkeys);
        if (!conflicts.IsEmpty)
        {
            throw new FormatException(
                $"{conflicts[0].First} and {conflicts[0].Second} use the same shortcut.");
        }

        return _currentSettings with
        {
            Audio = _currentSettings.Audio with
            {
                DeviceId = (_microphone.SelectedItem as
                    MacMicrophoneDevice)?.Id,
            },
            Transcription = _currentSettings.Transcription with
            {
                Model = Required(_model.SelectedItem as string, "Local model"),
                Language = Required(_language.SelectedItem as string, "Language"),
            },
            Text = new TextProcessingSettings(
                ParseMappings(
                    _vocabulary.Text,
                    static (left, right) => new VocabularyEntry(left, right),
                    "vocabulary"),
                ParseMappings(
                    _expansions.Text,
                    static (left, right) => new TextExpansion(left, right),
                    "text expansion"),
                _corrections.IsChecked == true,
                (WorkDomainPreset)(_workDomain.SelectedItem ?? WorkDomainPreset.General)),
            Insertion = new InsertionSettings(
                (TextInsertionMode)(_insertionMode.SelectedItem ?? TextInsertionMode.Paste),
                TimeSpan.FromMilliseconds((double)(_typingDelay.Value ?? 10))),
            Hotkeys = hotkeys,
            Preferences = new ApplicationPreferences(
                FirstRunCompleted:
                    _currentSettings.Preferences.FirstRunCompleted ||
                    completeFirstRun,
                StartWithWindows: _startAtLogin.IsChecked == true,
                HistoryEnabled: _history.IsChecked == true,
                HistoryLimit: checked((int)(_historyLimit.Value ?? 100))),
            SmartEdit = new SmartEditSettings(
                Enabled: _smartEnabled.IsChecked == true,
                SmartEditProviderKind.OpenAIResponses,
                Required(_smartEndpoint.Text, "Endpoint"),
                Required(_smartModel.Text, "Smart Edit model"),
                TimeSpan.FromSeconds((double)(_smartTimeout.Value ?? 30)),
                checked((int)(_smartRetries.Value ?? 1)),
                NullIfWhiteSpace(_smartInstructions.Text)),
        };
    }

    private static ImmutableArray<T> ParseMappings<T>(
        string? text,
        Func<string, string, T> create,
        string label)
    {
        var results = ImmutableArray.CreateBuilder<T>();
        string[] lines = (text ?? string.Empty).Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        foreach (string line in lines)
        {
            int separator = line.IndexOf("=>", StringComparison.Ordinal);
            if (separator <= 0 || separator + 2 >= line.Length)
            {
                throw new FormatException(
                    $"Each {label} line must use ‘left => right’. Invalid line: {line}");
            }

            results.Add(create(
                line[..separator].Trim(),
                line[(separator + 2)..].Trim()));
        }

        return results.ToImmutable();
    }

    private static StackPanel CreateForm() => new()
    {
        Spacing = 12,
        Margin = new Thickness(10),
    };

    private static ScrollViewer Wrap(Control content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
    };

    private static TabItem CreateTab(string header, Control content) => new()
    {
        Header = header,
        Content = content,
    };

    private static void AddHeading(Panel panel, string text) =>
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 8, 0, 0),
        });

    private static void AddField(
        Panel panel,
        string label,
        Control control)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("180,*"),
            ColumnSpacing = 12,
        };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        panel.Children.Add(grid);
    }

    private static string Required(string? value, string label) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new FormatException($"{label} is required.")
            : value.Trim();

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string GetActionLabel(HotkeyAction action) => action switch
    {
        HotkeyAction.Dictation => "Dictation",
        HotkeyAction.SmartEdit => "Smart Edit",
        HotkeyAction.TypingMode => "Typing Mode",
        HotkeyAction.Cancel => "Cancel",
        _ => action.ToString(),
    };
}

public sealed class MacSettingsApplyEventArgs(
    DictaCloneSettings settings,
    string? apiKey,
    bool completedFirstRun)
    : EventArgs
{
    public DictaCloneSettings Settings { get; } = settings;

    public string? ApiKey { get; } = apiKey;

    public bool CompletedFirstRun { get; } = completedFirstRun;
}

public sealed class MacPermissionRequestEventArgs(string permission)
    : EventArgs
{
    public string Permission { get; } = permission;
}
