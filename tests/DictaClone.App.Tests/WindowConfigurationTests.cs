using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using DictaClone.App.Presentation;
using DictaClone.Audio;
using DictaClone.Core.Contracts;
using DictaClone.Core.Hotkeys;
using DictaClone.Core.Settings;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfDataGrid = System.Windows.Controls.DataGrid;
using WpfLabel = System.Windows.Controls.Label;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfPasswordBox = System.Windows.Controls.PasswordBox;
using WpfTabControl = System.Windows.Controls.TabControl;

namespace DictaClone.App.Tests;

public sealed class WindowConfigurationTests
{
    [Fact]
    public async Task Overlay_IsNonActivatingTopmostAndNotFocusable()
    {
        await RunOnStaAsync(() =>
        {
            var overlay = new StatusOverlayWindow();

            Assert.False(overlay.ShowActivated);
            Assert.False(overlay.ShowInTaskbar);
            Assert.False(overlay.Focusable);
            Assert.False(overlay.IsHitTestVisible);
            Assert.True(overlay.Topmost);
            Assert.Equal(WindowStyle.None, overlay.WindowStyle);

            overlay.ShowStatus(OverlayStatus.Recording);
            Assert.True(overlay.IsVisible);
            Assert.True(overlay.HasNoActivateExtendedStyle);
            Assert.False(overlay.IsKeyboardFocusWithin);

            overlay.HideStatus();
            Assert.False(overlay.IsVisible);
            overlay.Close();
        });
    }

    [Fact]
    public async Task SettingsWindow_LoadsDefaultBindingsOnStaThread()
    {
        await RunOnStaAsync(() =>
        {
            var window = new SettingsWindow(HotkeyDefaults.Bindings);

            Assert.Equal("DictaClone settings", window.Title);
            Assert.Equal(
                HotkeyDefaults.Bindings.Length,
                window.Bindings.Length);
            Assert.Equal(
                HotkeyDefaults.Bindings.ToArray(),
                window.Bindings.ToArray());

            window.Close();
        });
    }

    [Fact]
    public async Task SettingsWindow_LoadsRuntimeAudioAndSpeechChoices()
    {
        await RunOnStaAsync(() =>
        {
            AudioSettings audio = DictaCloneSettings.Default.Audio with
            {
                DeviceId = "microphone-2",
                SilenceThreshold = 0.025,
            };
            TranscriptionSettings transcription =
                DictaCloneSettings.Default.Transcription with
                {
                    Model = "small.en",
                    Language = "auto",
                };
            var insertion = new InsertionSettings(
                TextInsertionMode.DelayedTyping,
                TimeSpan.FromMilliseconds(35));
            var devices = new[]
            {
                new MicrophoneDeviceInfo(
                    "microphone-2",
                    "Test microphone",
                    IsDefault: false),
            };
            var window = new SettingsWindow(
                HotkeyDefaults.Bindings,
                audio,
                transcription,
                devices,
                insertion);

            Assert.Equal(audio, window.Audio);
            Assert.Equal(transcription, window.Transcription);
            Assert.Equal(insertion, window.Insertion);

            window.Close();
        });
    }

    [Fact]
    public async Task SettingsWindow_ExposesKnowledgePrivacyAndAccessibleTabs()
    {
        await RunOnStaAsync(() =>
        {
            TextProcessingSettings text = DictaCloneSettings.Default.Text with
            {
                WorkDomain = WorkDomainPreset.Business,
                Vocabulary = [new("jay son", "JSON")],
                Expansions = [new("signature", "Kind regards")],
            };
            ApplicationPreferences preferences = new(
                FirstRunCompleted: false,
                StartWithWindows: true,
                HistoryEnabled: true,
                HistoryLimit: 42);
            var window = new SettingsWindow(
                HotkeyDefaults.Bindings,
                textSettings: text,
                preferences: preferences,
                firstRun: true);
            TextProcessingSettings? submittedText = null;
            ApplicationPreferences? submittedPreferences = null;
            window.TextSettingsChanged +=
                (_, eventArgs) => submittedText = eventArgs.Settings;
            window.PreferencesChanged +=
                (_, eventArgs) => submittedPreferences = eventArgs.Preferences;

            var tabs = Assert.IsType<WpfTabControl>(window.Content);
            Assert.Equal(
                ["General", "Knowledge", "Smart Edit", "Privacy & recovery"],
                tabs.Items.Cast<TabItem>()
                    .Select(tab => Assert.IsType<string>(tab.Header))
                    .ToArray());
            Assert.Equal(
                "DictaClone settings sections",
                AutomationProperties.GetName(tabs));
            Assert.Equal(text, window.Text);
            Assert.Equal(preferences, window.Preferences);
            Assert.Equal(DictaCloneSettings.Default.SmartEdit, window.SmartEdit);
            Assert.Equal(2, FindLogicalChildren<WpfDataGrid>(window).Count());
            Assert.All(
                FindLogicalChildren<WpfLabel>(window),
                label => Assert.NotNull(label.Target));

            FindButton(window, "Apply knowledge").RaiseEvent(
                new RoutedEventArgs(WpfButton.ClickEvent));
            FindButton(window, "Complete setup").RaiseEvent(
                new RoutedEventArgs(WpfButton.ClickEvent));

            Assert.NotNull(submittedText);
            Assert.Equivalent(text, submittedText, strict: true);
            Assert.NotNull(submittedPreferences);
            Assert.True(submittedPreferences.FirstRunCompleted);
            Assert.True(submittedPreferences.StartWithWindows);
            Assert.True(submittedPreferences.HistoryEnabled);
            Assert.Equal(42, submittedPreferences.HistoryLimit);
            window.Close();
        });
    }

    [Fact]
    public async Task HistoryWindow_SortsCopiesAndRequestsClear()
    {
        await RunOnStaAsync(() =>
        {
            TranscriptHistoryEntry older = new(
                new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
                "older entry");
            TranscriptHistoryEntry newer = new(
                new DateTimeOffset(2026, 8, 1, 13, 0, 0, TimeSpan.Zero),
                "newer entry");
            var window = new HistoryWindow([older, newer]);
            TranscriptHistoryEntry? copied = null;
            int clearRequests = 0;
            window.CopyRequested +=
                (_, eventArgs) => copied = eventArgs.Entry;
            window.ClearRequested += (_, _) => clearRequests++;

            WpfListBox entries = Assert.Single(
                FindLogicalChildren<WpfListBox>(window));
            Assert.Equal("Saved transcripts", AutomationProperties.GetName(entries));
            Assert.Equal(2, window.EntryCount);
            Assert.Contains("newer entry", entries.Items[0]!.ToString());
            entries.SelectedIndex = 0;
            FindButton(window, "Copy selected").RaiseEvent(
                new RoutedEventArgs(WpfButton.ClickEvent));
            FindButton(window, "Clear history").RaiseEvent(
                new RoutedEventArgs(WpfButton.ClickEvent));

            Assert.Equal(newer, copied);
            Assert.Equal(1, clearRequests);
            window.Close();
        });
    }

    [Fact]
    public async Task SmartEditTab_RequiresKeyAndRaisesExplicitConsentSettings()
    {
        await RunOnStaAsync(() =>
        {
            var window = new SettingsWindow(
                HotkeyDefaults.Bindings,
                smartEditSettings: DictaCloneSettings.Default.SmartEdit,
                smartEditCredentialStored: false);
            SmartEditSettingsChangedEventArgs? submitted = null;
            window.SmartEditSettingsChanged +=
                (_, eventArgs) => submitted = eventArgs;
            WpfCheckBox enabled = Assert.Single(
                FindLogicalChildren<WpfCheckBox>(window),
                control => AutomationProperties.GetName(control) ==
                    "Enable cloud Smart Edit");
            WpfPasswordBox apiKey = Assert.Single(
                FindLogicalChildren<WpfPasswordBox>(window),
                control => AutomationProperties.GetName(control) ==
                    "Smart Edit API key");
            enabled.IsChecked = true;

            FindButton(window, "Apply Smart Edit settings").RaiseEvent(
                new RoutedEventArgs(WpfButton.ClickEvent));
            Assert.Null(submitted);

            apiKey.Password = "test-api-key";
            FindButton(window, "Apply Smart Edit settings").RaiseEvent(
                new RoutedEventArgs(WpfButton.ClickEvent));

            Assert.NotNull(submitted);
            Assert.True(submitted.Settings.Enabled);
            Assert.Equal("test-api-key", submitted.ApiKey);
            Assert.False(submitted.DeleteCredential);
            Assert.Empty(apiKey.Password);
            window.Close();
        });
    }

    [Fact]
    public void OverlayPlacement_UsesTheSelectedMonitorWorkArea()
    {
        (int x, int y) = StatusOverlayWindow.CalculateBottomCenterPosition(
            left: -1920,
            top: 0,
            right: 0,
            bottom: 1040,
            overlayWidth: 400,
            overlayHeight: 60,
            bottomMargin: 42);

        Assert.Equal(-1160, x);
        Assert.Equal(938, y);
    }

    private static Task RunOnStaAsync(Action action)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static WpfButton FindButton(
        DependencyObject root,
        string content) =>
        Assert.Single(
            FindLogicalChildren<WpfButton>(root),
            button => string.Equals(
                button.Content?.ToString(),
                content,
                StringComparison.Ordinal));

    private static IEnumerable<T> FindLogicalChildren<T>(
        DependencyObject root)
        where T : DependencyObject
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is T typed)
            {
                yield return typed;
            }

            if (child is DependencyObject dependencyObject)
            {
                foreach (T descendant in
                    FindLogicalChildren<T>(dependencyObject))
                {
                    yield return descendant;
                }
            }
        }
    }
}
