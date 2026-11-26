using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace KeySaver
{
    public partial class MainWindow
    {
        private void OpenSettings(object sender, RoutedEventArgs e)
        {
            HistoryView.Visibility = Visibility.Collapsed;
            SettingsView.Visibility = Visibility.Visible;
        }

        private void CloseSettings(object sender, RoutedEventArgs e)
        {
            SettingsView.Visibility = Visibility.Collapsed;
            HistoryView.Visibility = Visibility.Visible;
        }

        private void CenterWindow(object sender, RoutedEventArgs e)
        {
            var area = SystemParameters.WorkArea;
            if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
            Left = area.Left + Math.Max(0, (area.Width - ActualWidth) / 2);
            Top = area.Top + Math.Max(0, (area.Height - ActualHeight) / 2);
        }

        private void FilterChanged(object sender, RoutedEventArgs e)
        {
            if (!ready || rendering) return;
            if (ReferenceEquals(sender, AppList))
            {
                var item = AppList.SelectedItem as AppItem;
                if (item == null) return;
                appFilter = item.Key;
            }
            RenderHistory(false);
        }

        private void ResetFilters(object sender, RoutedEventArgs e)
        {
            appFilter = "all";
            rendering = true;
            TextSearch.Clear();
            DateFilter.SelectedIndex = 0;
            FromDate.SelectedDate = null;
            ToDate.SelectedDate = null;
            rendering = false;
            RenderHistory(false);
            TextSearch.Focus();
        }

        private void ToggleSort(object sender, RoutedEventArgs e)
        {
            oldestFirst = !oldestFirst;
            SortButton.ToolTip = oldestFirst ? "Сначала старые" : "Сначала новые";
            ((System.Windows.Media.ScaleTransform)SortIcon.RenderTransform).ScaleY = oldestFirst ? -1 : 1;
            RenderHistory(false);
        }

        private void SessionSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ready || rendering) return;
            ShowPreview(SessionList.SelectedItem as SessionView);
            UpdateCaption();
        }

        private void CopyText(object sender, RoutedEventArgs e)
        {
            if (preview == null || string.IsNullOrEmpty(preview.Text)) return;
            try { Clipboard.SetText(preview.Text); }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                ShowError("Не удалось скопировать текст.");
            }
        }

        private void RenderHistory(bool selectFirst)
        {
            rendering = true;
            try
            {
                var kept = new HashSet<long>();
                long? current = null;
                if (!selectFirst)
                {
                    foreach (var item in SessionList.SelectedItems.OfType<SessionView>()) kept.Add(item.Record.Id);
                    if (preview != null) current = preview.Record.Id;
                }

                var appQuery = AppSearch.Text.Trim();
                int total = historyReady ? storage.SessionCount() : 0;
                var rows = new List<AppItem> { HistoryCatalog.AllApps(total) };
                if (historyReady)
                {
                    foreach (var group in storage.Apps().OrderBy(group => HistoryCatalog.Find(group.App).Name, StringComparer.CurrentCultureIgnoreCase))
                    {
                        var item = HistoryCatalog.Find(group.App, group.Count, group.Path);
                        if (appQuery.Length == 0 || item.Matches(appQuery)) rows.Add(item);
                    }
                }
                EmptyAppSearch.Visibility = appQuery.Length > 0 && rows.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
                AppList.ItemsSource = rows;
                AppList.SelectedItem = rows.FirstOrDefault(item => string.Equals(item.Key, appFilter, StringComparison.OrdinalIgnoreCase));

                var tag = (DateFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
                DateRange.Visibility = tag == "range" ? Visibility.Visible : Visibility.Collapsed;
                var today = DateTime.Today;
                DateTime? from = null;
                DateTime? to = null;
                bool reversed = false;
                if (tag == "today") from = to = today;
                else if (tag == "yesterday") from = to = today.AddDays(-1);
                else if (tag == "week") { from = today.AddDays(-6); to = today; }
                else if (tag == "range")
                {
                    from = FromDate.SelectedDate;
                    to = ToDate.SelectedDate;
                    reversed = from.HasValue && to.HasValue && from.Value.Date > to.Value.Date;
                }
                RangeError.Visibility = reversed ? Visibility.Visible : Visibility.Collapsed;

                var text = TextSearch.Text.Trim();
                var views = new List<SessionView>();
                if (!reversed && historyReady)
                {
                    foreach (var record in storage.Query(appFilter, from, to, text))
                        views.Add(new SessionView(record, HistoryCatalog.Find(record.App, 0, record.Path), today, oldestFirst));
                }

                var listed = new ListCollectionView(views);
                listed.SortDescriptions.Add(new SortDescription(nameof(SessionView.GroupOrder), ListSortDirection.Ascending));
                listed.SortDescriptions.Add(new SortDescription(nameof(SessionView.Start),
                    oldestFirst ? ListSortDirection.Ascending : ListSortDirection.Descending));
                listed.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SessionView.GroupOrder)));
                SessionList.ItemsSource = listed;

                if (selectFirst)
                {
                    if (listed.Count > 0) SessionList.SelectedItem = listed.GetItemAt(0);
                }
                else
                {
                    foreach (SessionView item in listed)
                        if (kept.Contains(item.Record.Id)) SessionList.SelectedItems.Add(item);
                }

                var selected = SessionList.SelectedItems.OfType<SessionView>().FirstOrDefault(item => current.HasValue && item.Record.Id == current.Value)
                    ?? SessionList.SelectedItem as SessionView;
                ShowPreview(selected);
                UpdateCaption();
                bool shown = listed.Count > 0;
                bool any = total > 0;
                bool filtered = appFilter != "all" || tag != "all" || TextSearch.Text.Length > 0;
                SessionList.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
                EmptyHistory.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
                EmptyHistoryTitle.Text = any ? "Сессии не найдены" : "Сессий пока нет";
                EmptyHistoryHint.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
                EmptyHistoryReset.Visibility = any && filtered ? Visibility.Visible : Visibility.Collapsed;
                ResetFiltersButton.Visibility = filtered ? Visibility.Visible : Visibility.Collapsed;
            }
            finally { rendering = false; }
        }

        private void ShowPreview(SessionView session)
        {
            preview = session;
            DetailContent.DataContext = session;
            DetailContent.Visibility = session == null ? Visibility.Collapsed : Visibility.Visible;
            EmptyDetail.Visibility = session == null ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateCaption()
        {
            int selected = SessionList.SelectedItems.Count;
            string text = SessionLabel(SessionList.Items.Count);
            SessionCount.Text = selected > 1 ? text + " · Выбрано " + selected : text;
            DeleteButton.IsEnabled = selected > 0;
        }

        private void PublishRules()
        {
            if (recorder == null) return;
            var closed = recorder.Apply(preferences);
            if (closed != null && preferences.RecordingEnabled) OnSessionRecorded(closed);
        }

        private void RenderScope()
        {
            bool limited = preferences.Mode != "all";
            ScopeOptions.Visibility = limited ? Visibility.Visible : Visibility.Collapsed;
            if (!limited) return;
            ScopeListTitle.Text = preferences.Mode == "except" ? "Исключённые приложения" : "Выбранные приложения";
            ScopeList.ItemsSource = (preferences.Mode == "except" ? preferences.ExceptPaths : preferences.OnlyPaths).ToArray();
        }

        private void ScopeChoiceChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ready || rendering) return;
            var item = ScopeChoice.SelectedItem as AppItem;
            CustomPathField.Visibility = item != null && item.Key == "custom" ? Visibility.Visible : Visibility.Collapsed;
            ScopeMessage.Text = string.Empty;
        }

        private void AddScopeApp(object sender, RoutedEventArgs e)
        {
            if (preferences.Mode == "all") return;
            var item = ScopeChoice.SelectedItem as AppItem;
            if (item == null) return;
            AddScopePath(item.Key == "custom" ? CustomPathInput.Text : item.Path);
        }

        private void BrowseScopeApp(object sender, RoutedEventArgs e)
        {
            if (preferences.Mode == "all") return;
            var dialog = new OpenFileDialog
            {
                Title = "Выбор приложения",
                Filter = "Программы (*.exe)|*.exe",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true) return;
            AddScopePath(dialog.FileName);
        }

        private void AddScopePath(string path)
        {
            path = NormalizeExe(path);
            if (!IsExe(path))
            {
                ScopeMessage.Text = "Укажите полный путь к файлу .exe.";
                return;
            }
            var list = preferences.Mode == "except" ? preferences.ExceptPaths : preferences.OnlyPaths;
            if (list.Any(entry => string.Equals(entry, path, StringComparison.OrdinalIgnoreCase)))
            {
                ScopeMessage.Text = "Это приложение уже есть в списке.";
                return;
            }
            list.Add(path);
            CustomPathInput.Clear();
            ScopeChoice.SelectedItem = null;
            CustomPathField.Visibility = Visibility.Collapsed;
            ScopeMessage.Text = string.Empty;
            RenderScope();
            SavePreferences();
            PublishRules();
        }

        private void RemoveScopeApp(object sender, RoutedEventArgs e)
        {
            var path = ((FrameworkElement)sender).Tag as string;
            var list = preferences.Mode == "except" ? preferences.ExceptPaths : preferences.OnlyPaths;
            list.RemoveAll(entry => string.Equals(entry, path, StringComparison.OrdinalIgnoreCase));
            RenderScope();
            SavePreferences();
            PublishRules();
        }

        private void IdleSecondsChanged(object sender, TextChangedEventArgs e)
        {
            if (!ready) return;
            if (!ValidateIdleSeconds()) return;
            SavePreferences();
            PublishRules();
        }

        private bool ValidateIdleSeconds()
        {
            int seconds;
            bool ok = int.TryParse(IdleSecondsInput.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out seconds) && seconds >= 1;
            IdleError.Visibility = preferences.IdleRule && !ok ? Visibility.Visible : Visibility.Collapsed;
            if (ok) preferences.IdleSeconds = seconds;
            return !preferences.IdleRule || ok;
        }

        private void ShowError(string message)
        {
            var dialog = CreateDialog("KeySaver", "Не удалось выполнить действие", message, out var actions);
            var close = DialogButton("Закрыть", PaletteBrush("AccentBrush"), PaletteBrush("OnAccentBrush"), PaletteBrush("AccentBrush"));
            close.IsDefault = true;
            close.IsCancel = true;
            actions.Children.Add(close);
            dialog.ShowDialog();
        }

        private static string NormalizeExe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Trim('"').Replace('/', '\\');
        }

        private static bool IsExe(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && path.IndexOfAny(Path.GetInvalidPathChars()) < 0
                && Path.IsPathRooted(path)
                && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }
    }
}
