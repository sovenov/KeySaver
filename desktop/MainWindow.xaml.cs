using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace KeySaver
{
    public partial class MainWindow : Window
    {
        private readonly WorkspaceStorage storage;
        private Preferences preferences;
        private SessionView preview;
        private string appFilter = "all";
        private bool ready;
        private bool rendering;
        private bool oldestFirst;
        private bool canSaveHistory = true;
        private bool canSavePreferences = true;
        private KeyboardRecorder recorder;

        public MainWindow() : this(new WorkspaceStorage()) { }

        internal MainWindow(WorkspaceStorage storage)
        {
            this.storage = storage;
            InitializeComponent();

            var loadErrors = new List<string>();
            bool firstRun = !storage.HasPreferences();
            try
            {
                preferences = storage.LoadPreferences();
            }
            catch (Exception error) when (IsStorageError(error))
            {
                preferences = new Preferences();
                canSavePreferences = false;
                loadErrors.Add("Не удалось открыть файл настроек. " + error.Message);
            }

            DateFilter.SelectedIndex = 0;
            ExportFormat.SelectedIndex = 0;
            AllMode.IsChecked = preferences.Mode == "all";
            ExceptMode.IsChecked = preferences.Mode == "except";
            OnlyMode.IsChecked = preferences.Mode == "only";
            RecordingEnabled.IsChecked = preferences.RecordingEnabled;
            SessionRuleControls.IsEnabled = preferences.RecordingEnabled;
            MouseRule.IsChecked = preferences.MouseRule;
            TabRule.IsChecked = preferences.TabRule;
            EnterRule.IsChecked = preferences.EnterRule;
            IdleRule.IsChecked = preferences.IdleRule;
            IdleSecondsInput.Text = preferences.IdleSeconds.ToString(CultureInfo.InvariantCulture);
            IdleSecondsInput.IsEnabled = preferences.IdleRule;
            try
            {
                if (firstRun) WindowsAutostart.Set(true);
                Autostart.IsChecked = WindowsAutostart.IsEnabled();
                if (Autostart.IsChecked == true) WindowsAutostart.Set(true);
            }
            catch (Exception error) when (error is UnauthorizedAccessException || error is System.Security.SecurityException || error is System.IO.IOException)
            {
                Autostart.IsChecked = false;
                loadErrors.Add("Не удалось прочитать автозапуск. " + error.Message);
            }

            recorder = new KeyboardRecorder(OnSessionRecorded, OnDraftChanged);
            recorder.Apply(preferences);

            ready = true;
            ApplyTheme();
            RenderHistory(true);
            RenderScope();
            UpdateExportOptions();
            ShowImportFile();
            Loaded += (sender, args) =>
            {
                if (loadErrors.Count > 0) ShowError(string.Join("\n\n", loadErrors));
                if (!UiPassword.IsSet(preferences))
                {
                    if (!PromptCreatePassword()) ExitApplication();
                    else OpenHistory();
                }
                else
                {
                    LockInterface(null, null);
                }
            };
            CreateTrayIcon();
        }

        private void OnDraftChanged(TypingDraft draft)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnDraftChanged(draft)));
                return;
            }
            if (!historyReady) return;
            try
            {
                if (draft == null || TypingDraft.IsBlank(draft.Text)) storage.ClearDraft();
                else storage.SaveDraft(draft);
            }
            catch (Exception error) when (IsStorageError(error)) { }
        }

        private void OnSessionRecorded(SessionRecord rec)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnSessionRecorded(rec)));
                return;
            }
            try { storage.CommitSession(rec); RenderHistory(false); }
            catch (Exception error) when (IsStorageError(error))
            {
                ShowError("Не удалось сохранить сессию. " + error.Message);
            }
        }

        private void ChangeAutostart(object sender, RoutedEventArgs e)
        {
            if (!ready) return;
            try
            {
                WindowsAutostart.Set(Autostart.IsChecked == true);
            }
            catch (Exception error) when (error is UnauthorizedAccessException || error is System.Security.SecurityException || error is System.IO.IOException)
            {
                ready = false;
                Autostart.IsChecked = WindowsAutostart.IsEnabled();
                ready = true;
                ShowError("Не удалось изменить автозапуск. " + error.Message);
            }
        }

        private void ChangeRecordingEnabled(object sender, RoutedEventArgs e)
        {
            if (!ready) return;
            SetRecording(RecordingEnabled.IsChecked == true);
        }

        private void SetRecording(bool enabled)
        {
            if (!ready) return;
            if (RecordingEnabled.IsChecked != enabled)
            {
                ready = false;
                RecordingEnabled.IsChecked = enabled;
                ready = true;
            }
            preferences.RecordingEnabled = enabled;
            SessionRuleControls.IsEnabled = enabled;
            if (enabled)
            {
                recorder.Apply(preferences);
                var recorderError = recorder.Start();
                if (!string.IsNullOrEmpty(recorderError)) ShowError(recorderError);
            }
            else
            {
                var pending = recorder.Stop();
                if (pending != null) OnSessionRecorded(pending);
            }
            SavePreferences();
            UpdateTrayMenu();
        }

        private void ChangeMode(object sender, RoutedEventArgs e)
        {
            if (!ready) return;
            preferences.Mode = (string)((RadioButton)sender).Tag;
            ScopeChoice.SelectedItem = null;
            CustomPathInput.Clear();
            RenderScope();
            SavePreferences();
            PublishRules();
        }

        private void ChangeRules(object sender, RoutedEventArgs e)
        {
            if (!ready) return;
            preferences.MouseRule = MouseRule.IsChecked == true;
            preferences.TabRule = TabRule.IsChecked == true;
            preferences.EnterRule = EnterRule.IsChecked == true;
            preferences.IdleRule = IdleRule.IsChecked == true;
            IdleSecondsInput.IsEnabled = preferences.IdleRule;
            ValidateIdleSeconds();
            SavePreferences();
            PublishRules();
        }

        protected override void OnClosed(EventArgs e)
        {
            DisposeTrayIcon();
            var pending = recorder?.Stop();
            recorder?.Dispose();
            recorder = null;
            if (pending != null && historyReady)
            {
                try { storage.CommitSession(pending); }
                catch (Exception error) when (IsStorageError(error)) { }
            }
            base.OnClosed(e);
        }


    }

    internal static class WindowsAutostart
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "KeySaver";

        public static bool IsEnabled()
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(KeyPath, false))
                return key != null && key.GetValue(ValueName) is string path && path.Length > 0;
        }

        public static void Set(bool enabled)
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                if (key == null) throw new System.IO.IOException("Не удалось открыть параметры автозапуска.");
                if (!enabled)
                {
                    key.DeleteValue(ValueName, false);
                    return;
                }
                var exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                key.SetValue(ValueName, "\"" + exe + "\"");
            }
        }
    }
}
