using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace KeySaver
{
    public partial class MainWindow
    {
        private TransferDocument importDocument;
        private bool importing;
        private bool exporting;

        private void TransferOptionsChanged(object sender, RoutedEventArgs e)
        {
            if (ready) UpdateExportOptions();
        }

        private void UpdateExportOptions()
        {
            bool archive = ExportFormat.SelectedIndex == 1;
            bool protectedArchive = archive && ProtectArchive.IsChecked == true;
            ExportJsonNote.Visibility = archive ? Visibility.Collapsed : Visibility.Visible;
            ArchiveOptions.Visibility = archive ? Visibility.Visible : Visibility.Collapsed;
            ExportPasswordFields.Visibility = protectedArchive ? Visibility.Visible : Visibility.Collapsed;
            if (!protectedArchive)
            {
                ExportPassword.Clear();
                ExportRepeat.Clear();
            }
            ExportStatus.Text = string.Empty;
        }

        private async void ExportHistory(object sender, RoutedEventArgs e)
        {
            if (exporting) return;
            bool archive = ExportFormat.SelectedIndex == 1;
            bool protect = archive && ProtectArchive.IsChecked == true;
            string password = protect ? ExportPassword.Password : string.Empty;
            if (protect && (password.Length == 0 || password != ExportRepeat.Password))
            {
                SetTransferStatus(ExportStatus, password.Length == 0 ? "Введите пароль архива." : "Пароли не совпадают.", true);
                return;
            }

            string extension = archive ? ".keysaver" : ".json";
            var dialog = new SaveFileDialog
            {
                Title = "Экспорт истории",
                DefaultExt = extension,
                Filter = archive ? "Архив KeySaver (*.keysaver)|*.keysaver" : "JSON (*.json)|*.json",
                FileName = $"KeySaver-{DateTime.Today:yyyy-MM-dd}",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != true) return;

            var action = (Button)sender;
            exporting = true;
            action.IsEnabled = false;
            SetTransferStatus(ExportStatus, "Подготовка файла…");
            try
            {
                var snapshot = sessions;
                string path = dialog.FileName;
                await Task.Run(() =>
                {
                    string text = archive ? HistoryTransfer.ExportArchive(snapshot, password) : HistoryTransfer.ExportJson(snapshot);
                    WorkspaceStorage.WriteText(path, text);
                });
                ExportPassword.Clear();
                ExportRepeat.Clear();
                SetTransferStatus(ExportStatus, "Файл истории сохранён.");
            }
            catch (Exception error) when (IsStorageError(error))
            {
                SetTransferStatus(ExportStatus, error.Message, true);
            }
            finally
            {
                exporting = false;
                action.IsEnabled = true;
            }
        }

        private void ChooseImportFile(object sender, RoutedEventArgs e)
        {
            if (importing) return;
            var dialog = new OpenFileDialog
            {
                Title = "Импорт истории",
                Filter = "Файлы KeySaver (*.json;*.keysaver)|*.json;*.keysaver|JSON (*.json)|*.json|Архив KeySaver (*.keysaver)|*.keysaver",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true) return;

            importDocument = null;
            ImportFileName.Text = Path.GetFileName(dialog.FileName);
            ImportPassword.Clear();
            try
            {
                using (var stream = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length > HistoryTransfer.MaxFileBytes) throw new InvalidDataException("Размер файла превышает 16 МБ.");
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), false))
                        importDocument = HistoryTransfer.Read(reader.ReadToEnd());
                }
                string format = importDocument.Kind == "json" ? "Файл JSON." :
                    importDocument.RequiresPassword ? "Архив KeySaver с паролем." : "Архив KeySaver без пароля.";
                SetTransferStatus(ImportStatus, format);
            }
            catch (Exception error) when (IsStorageError(error))
            {
                SetTransferStatus(ImportStatus, error.Message, true);
            }
            ShowImportFile();
        }

        private void ShowImportFile()
        {
            ImportPasswordField.Visibility = importDocument?.RequiresPassword == true ? Visibility.Visible : Visibility.Collapsed;
            ImportButton.IsEnabled = importDocument != null && canSaveHistory && !importing;
        }

        private async void ImportHistory(object sender, RoutedEventArgs e)
        {
            if (importDocument == null || importing) return;
            var document = importDocument;
            string password = document.RequiresPassword ? ImportPassword.Password : string.Empty;
            if (document.RequiresPassword && password.Length == 0)
            {
                SetTransferStatus(ImportStatus, "Введите пароль архива.", true);
                return;
            }

            importing = true;
            ShowImportFile();
            SetTransferStatus(ImportStatus, "Импорт истории…");
            try
            {
                var decoded = await Task.Run(() => document.Decode(password));
                var identities = new System.Collections.Generic.HashSet<Tuple<string, DateTimeOffset, DateTimeOffset, string>>(sessions.Select(SessionIdentity));
                var added = decoded.Where(record => identities.Add(SessionIdentity(record))).ToArray();
                if (added.Length > 0 && !SaveHistory(sessions.Concat(added).ToArray())) return;

                ImportPassword.Clear();
                SetTransferStatus(ImportStatus, added.Length > 0 ? $"Импортировано: {SessionLabel(added.Length)}." :
                    decoded.Count == 0 ? "В файле нет сессий." : "Все сессии из файла уже есть в истории.");
            }
            catch (Exception error) when (IsStorageError(error))
            {
                SetTransferStatus(ImportStatus, error.Message, true);
            }
            finally
            {
                importing = false;
                ShowImportFile();
            }
        }

        private static Tuple<string, DateTimeOffset, DateTimeOffset, string> SessionIdentity(SessionRecord record)
        {
            return Tuple.Create(record.App, record.StartedAt, record.EndedAt, record.Text);
        }

        private void SetTransferStatus(TextBlock label, string text, bool error = false)
        {
            label.Text = text;
            label.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorBrush" : "MutedBrush");
        }
    }
}
