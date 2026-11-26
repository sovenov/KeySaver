using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeySaver
{
    public partial class MainWindow
    {
        private static readonly Dictionary<string, string> DarkColors = new Dictionary<string, string>
        {
            ["CanvasBrush"] = "#161D2A", ["TintBrush"] = "#25334D", ["SurfaceBrush"] = "#202838",
            ["InsetBrush"] = "#1A2230", ["SidebarBrush"] = "#1A2230", ["HeaderBrush"] = "#1E2736",
            ["TextBrush"] = "#E9EEF9", ["MutedBrush"] = "#B8C2D5", ["SubtleBrush"] = "#A7B3C9",
            ["LineBrush"] = "#354055", ["ActiveLineBrush"] = "#425987", ["AccentBrush"] = "#A2BAFF",
            ["OnAccentBrush"] = "#202838", ["SelectedBrush"] = "#2A3A58", ["HoverBrush"] = "#263348",
            ["ErrorBrush"] = "#FFA5B0", ["NoteBadgeBrush"] = "#304973", ["NoteGlyphBrush"] = "#AFC8FF",
            ["CodeBadgeBrush"] = "#28465D", ["CodeGlyphBrush"] = "#A6DBFF", ["EdgeBadgeBrush"] = "#24484A",
            ["EdgeGlyphBrush"] = "#9EE4DD", ["MailBadgeBrush"] = "#3C3C65", ["MailGlyphBrush"] = "#C8C3FF"
        };

        private Dictionary<string, Brush> lightColors;
        private bool darkTheme;

        private void ApplyTheme()
        {
            if (lightColors == null) lightColors = DarkColors.Keys.ToDictionary(key => key, PaletteBrush);
            darkTheme = preferences.Theme == "dark" || (preferences.Theme == "system" && SystemUsesDarkTheme());
            foreach (var pair in DarkColors) Resources[pair.Key] = darkTheme ? BrushOf(pair.Value) : lightColors[pair.Key];
            ThemeIcon.Data = (Geometry)FindResource(darkTheme ? "SunGeometry" : "MoonGeometry");
        }

        private static bool SystemUsesDarkTheme()
        {
            try
            {
                var setting = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", null);
                if (setting is int value) return value == 0;
            }
            catch (Exception)
            {
                // Fall back to the current Windows color scheme.
            }
            var background = SystemColors.WindowColor;
            return background.R * 299 + background.G * 587 + background.B * 114 < 128000;
        }

        private void ToggleTheme(object sender, RoutedEventArgs e)
        {
            preferences.Theme = darkTheme ? "light" : "dark";
            ApplyTheme();
            SavePreferences();
        }

        private void ShowAbout(object sender, RoutedEventArgs e)
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            var dialog = CreateDialog("О программе", "KeySaver " + version,
                "Локальный журнал ввода с клавиатуры для Windows. Сессия сохраняется после щелчка левой кнопкой мыши, Tab или паузы.\n\n" +
                "Форматы переноса: JSON и архив KeySaver (.keysaver), в том числе с паролем.", out var actions,
                "C# · WPF · .NET Framework 4.8 · Windows CNG");
            var close = DialogButton("Закрыть", PaletteBrush("AccentBrush"), PaletteBrush("OnAccentBrush"), PaletteBrush("AccentBrush"));
            close.IsDefault = true;
            close.IsCancel = true;
            actions.Children.Add(close);
            dialog.Loaded += (window, args) => close.Focus();
            dialog.ShowDialog();
        }

        private void DeleteSessions(object sender, RoutedEventArgs e)
        {
            var selected = new HashSet<SessionRecord>(SessionList.SelectedItems.OfType<SessionView>().Select(item => item.Record));
            if (selected.Count == 0) return;

            string heading = selected.Count == 1 ? "Удалить сессию?" : "Удалить выбранные сессии?";
            string message = selected.Count == 1 ? "Сессия будет удалена из истории." : $"Будут удалены выбранные сессии ({selected.Count}).";
            var dialog = CreateDialog("Удаление сессий", heading, message, out var actions);
            var no = DialogButton("Нет", BrushOf(darkTheme ? "#2C4036" : "#EAF3ED"),
                BrushOf(darkTheme ? "#C1DFCB" : "#335C47"), BrushOf(darkTheme ? "#587465" : "#A7C1B0"));
            var yes = DialogButton("Да", BrushOf(darkTheme ? "#442F35" : "#F7EBEC"),
                BrushOf(darkTheme ? "#EFC6CA" : "#803E45"), BrushOf(darkTheme ? "#85565E" : "#D3AFB3"));
            no.IsDefault = true;
            no.IsCancel = true;
            yes.Click += (button, args) => dialog.DialogResult = true;
            actions.Children.Add(no);
            actions.Children.Add(yes);
            dialog.Loaded += (window, args) => no.Focus();
            if (dialog.ShowDialog() != true) return;

            try { SaveHistory(sessions.Where(session => !selected.Contains(session)).ToArray()); }
            catch (Exception error) when (IsStorageError(error)) { ShowError("Не удалось удалить сессии. " + error.Message); }
        }

        private Window CreateDialog(string title, string heading, string message, out StackPanel actions, string footer = null)
        {
            var content = new StackPanel { Margin = new Thickness(20) };
            content.Children.Add(new TextBlock { Text = heading, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock
            {
                Text = message, Foreground = PaletteBrush("MutedBrush"), TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 16)
            });
            actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            content.Children.Add(actions);
            if (footer != null)
            {
                content.Children.Add(new TextBlock
                {
                    Text = footer, FontSize = 11, Foreground = PaletteBrush("SubtleBrush"), TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 14, 0, 0)
                });
            }
            return new Window
            {
                Title = title, Owner = this, Width = 370, SizeToContent = SizeToContent.Height,
                ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = PaletteBrush("SurfaceBrush"), Foreground = PaletteBrush("TextBrush"),
                FontFamily = FontFamily, FontSize = FontSize, Content = content
            };
        }

        private Button DialogButton(string content, Brush background, Brush foreground, Brush border)
        {
            return new Button
            {
                Content = content, MinWidth = 72, MinHeight = 34, Margin = new Thickness(8, 0, 0, 0),
                Style = (Style)FindResource("RoundedButton"), Background = background, Foreground = foreground, BorderBrush = border
            };
        }

        private Brush PaletteBrush(string key) => (Brush)FindResource(key);

        private static SolidColorBrush BrushOf(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
        private void SavePreferences()
        {
            if (!ready || !canSavePreferences) return;
            try { storage.SavePreferences(preferences); }
            catch (Exception error) when (IsStorageError(error))
            {
                canSavePreferences = false;
                ShowError("Не удалось сохранить настройки. " + error.Message);
            }
        }

        private static bool IsStorageError(Exception error)
        {
            return error is System.IO.IOException
                || error is UnauthorizedAccessException
                || error is System.IO.InvalidDataException
                || error is System.Runtime.Serialization.SerializationException
                || error is System.Security.Cryptography.CryptographicException
                || error is System.Text.DecoderFallbackException;
        }

        private static string SessionLabel(int count)
        {
            int n = Math.Abs(count) % 100;
            int last = n % 10;
            string word = n > 10 && n < 20 ? "сессий" : last == 1 ? "сессия" : last >= 2 && last <= 4 ? "сессии" : "сессий";
            return count + " " + word;
        }
    }
}

