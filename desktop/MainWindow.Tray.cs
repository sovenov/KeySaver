using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace KeySaver
{
    public partial class MainWindow
    {
        private Forms.NotifyIcon trayIcon;
        private Drawing.Icon trayDrawingIcon;
        private Forms.ToolStripMenuItem enableCaptureItem;
        private Forms.ToolStripMenuItem disableCaptureItem;
        private IntPtr largeIconHandle;
        private IntPtr smallIconHandle;
        private bool closingForExit;

        private void CreateTrayIcon()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Открыть", null, (sender, args) => ShowMainWindow());
            enableCaptureItem = new Forms.ToolStripMenuItem("Включить захват", null, (sender, args) => SetRecording(true));
            disableCaptureItem = new Forms.ToolStripMenuItem("Выключить захват", null, (sender, args) => SetRecording(false));
            menu.Items.Add(enableCaptureItem);
            menu.Items.Add(disableCaptureItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Завершить", null, (sender, args) => ExitApplication());
            menu.Opening += (sender, args) => UpdateTrayMenu();

            trayDrawingIcon = LoadTrayIcon();
            trayIcon = new Forms.NotifyIcon
            {
                Icon = trayDrawingIcon,
                Text = "KeySaver",
                Visible = true,
                ContextMenuStrip = menu
            };
            trayIcon.MouseClick += (sender, args) =>
            {
                if (args.Button == Forms.MouseButtons.Left) ShowMainWindow();
            };
            UpdateTrayMenu();
        }

        private void ShowMainWindow()
        {
            ShowInTaskbar = true;
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitApplication()
        {
            closingForExit = true;
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!closingForExit)
            {
                e.Cancel = true;
                ShowInTaskbar = false;
                Hide();
                return;
            }
            base.OnClosing(e);
        }

        private void UpdateTrayMenu()
        {
            if (enableCaptureItem == null) return;
            bool enabled = preferences != null && preferences.RecordingEnabled;
            enableCaptureItem.Enabled = !enabled;
            disableCaptureItem.Enabled = enabled;
        }

        private void DisposeTrayIcon()
        {
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayIcon = null;
            }
            if (trayDrawingIcon != null)
            {
                trayDrawingIcon.Dispose();
                trayDrawingIcon = null;
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyWindowIcon();
        }

        private void ApplyWindowIcon()
        {
            var bitmap = LoadWindowBitmap();
            if (bitmap != null) Icon = bitmap;

            var exe = Assembly.GetExecutingAssembly().Location;
            ExtractIconEx(exe, 0, out largeIconHandle, out smallIconHandle, 1);
            if (largeIconHandle == IntPtr.Zero && trayDrawingIcon != null)
                largeIconHandle = CopyIcon(trayDrawingIcon.Handle);
            if (smallIconHandle == IntPtr.Zero && trayDrawingIcon != null)
                smallIconHandle = CopyIcon(trayDrawingIcon.Handle);

            var hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            if (source != null) source.AddHook(WindowMessageHook);
            if (smallIconHandle != IntPtr.Zero)
                SendMessage(hwnd, WM_SETICON, new IntPtr(ICON_SMALL), smallIconHandle);
            if (largeIconHandle != IntPtr.Zero)
                SendMessage(hwnd, WM_SETICON, new IntPtr(ICON_BIG), largeIconHandle);
        }

        private static BitmapSource LoadWindowBitmap()
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/KeySaver.ico", UriKind.Absolute));
            if (resource == null) return null;
            var copy = new MemoryStream();
            resource.Stream.CopyTo(copy);
            copy.Position = 0;
            using (copy)
            using (var icon = new Drawing.Icon(copy, 256, 256))
            {
                var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return image;
            }
        }

        private static Drawing.Icon LoadTrayIcon()
        {
            var exe = Assembly.GetExecutingAssembly().Location;
            var extracted = Drawing.Icon.ExtractAssociatedIcon(exe);
            if (extracted != null) return extracted;

            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/KeySaver.ico", UriKind.Absolute));
            return resource == null ? Drawing.SystemIcons.Application : new Drawing.Icon(resource.Stream);
        }

        private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WM_QUERYENDSESSION || message == WM_ENDSESSION)
                closingForExit = true;
            return IntPtr.Zero;
        }

        private const int WM_QUERYENDSESSION = 0x0011;
        private const int WM_ENDSESSION = 0x0016;
        private const int WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CopyIcon(IntPtr icon);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
    }
}
