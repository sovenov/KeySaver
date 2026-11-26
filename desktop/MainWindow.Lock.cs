using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KeySaver
{
    public partial class MainWindow
    {
        private Visibility historyBeforeLock = Visibility.Visible;
        private Visibility settingsBeforeLock = Visibility.Collapsed;

        private void LockInterface(object sender, RoutedEventArgs e)
        {
            if (!UiPassword.IsSet(preferences)) return;
            historyBeforeLock = HistoryView.Visibility;
            settingsBeforeLock = SettingsView.Visibility;
            AppHeader.Visibility = Visibility.Collapsed;
            HistoryView.Visibility = Visibility.Collapsed;
            SettingsView.Visibility = Visibility.Collapsed;
            UnlockPassword.Clear();
            UnlockError.Text = string.Empty;
            LockOverlay.Visibility = Visibility.Visible;
            UnlockPassword.Focus();
        }

        private void UnlockInterface(object sender, RoutedEventArgs e)
        {
            if (!UiPassword.Matches(preferences, UnlockPassword.Password))
            {
                UnlockError.Text = "Неверный пароль.";
                return;
            }
            UnlockPassword.Clear();
            UnlockError.Text = string.Empty;
            LockOverlay.Visibility = Visibility.Collapsed;
            AppHeader.Visibility = Visibility.Visible;
            HistoryView.Visibility = historyBeforeLock;
            SettingsView.Visibility = settingsBeforeLock;
        }

        private void UnlockPasswordKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) UnlockInterface(sender, e);
        }

        private void ChangePassword(object sender, RoutedEventArgs e)
        {
            if (!UiPassword.Matches(preferences, CurrentPassword.Password))
            {
                SetPasswordStatus("Текущий пароль указан неверно.", true);
                return;
            }
            if (NewPassword.Password.Length == 0 || NewPassword.Password != RepeatPassword.Password)
            {
                SetPasswordStatus(NewPassword.Password.Length == 0 ? "Введите новый пароль." : "Новые пароли не совпадают.", true);
                return;
            }
            UiPassword.Set(preferences, NewPassword.Password);
            SavePreferences();
            CurrentPassword.Clear();
            NewPassword.Clear();
            RepeatPassword.Clear();
            SetPasswordStatus("Пароль изменён.", false);
        }

        private void SetPasswordStatus(string text, bool error)
        {
            PasswordStatus.Text = text;
            PasswordStatus.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorBrush" : "MutedBrush");
        }

        private bool PromptCreatePassword()
        {
            var password = new PasswordBox { Style = (Style)FindResource("PasswordInput"), Margin = new Thickness(0, 0, 0, 8) };
            var repeat = new PasswordBox { Style = (Style)FindResource("PasswordInput") };
            var error = new TextBlock { Style = (Style)FindResource("StatusText"), Visibility = Visibility.Collapsed };
            var content = new StackPanel { Margin = new Thickness(20) };
            content.Children.Add(new TextBlock { Text = "Задайте пароль", FontSize = 18, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock
            {
                Text = "Он понадобится, чтобы разблокировать окно.",
                Foreground = PaletteBrush("MutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 14)
            });
            content.Children.Add(new TextBlock { Text = "Пароль", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            content.Children.Add(password);
            content.Children.Add(new TextBlock { Text = "Повторите пароль", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 6) });
            content.Children.Add(repeat);
            content.Children.Add(error);
            var save = DialogButton("Сохранить", PaletteBrush("AccentBrush"), PaletteBrush("OnAccentBrush"), PaletteBrush("AccentBrush"));
            save.IsDefault = true;
            save.HorizontalAlignment = HorizontalAlignment.Right;
            save.Margin = new Thickness(0, 16, 0, 0);
            content.Children.Add(save);
            var dialog = new Window
            {
                Title = "Пароль KeySaver", Owner = this, Width = 370, SizeToContent = SizeToContent.Height,
                ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = PaletteBrush("SurfaceBrush"), Foreground = PaletteBrush("TextBrush"),
                FontFamily = FontFamily, FontSize = FontSize, Content = content
            };
            save.Click += (sender, args) =>
            {
                if (password.Password.Length == 0 || password.Password != repeat.Password)
                {
                    error.Text = password.Password.Length == 0 ? "Введите пароль." : "Пароли не совпадают.";
                    error.Visibility = Visibility.Visible;
                    return;
                }
                UiPassword.Set(preferences, password.Password);
                SavePreferences();
                dialog.DialogResult = true;
            };
            dialog.Loaded += (sender, args) => password.Focus();
            return dialog.ShowDialog() == true;
        }
    }
}
