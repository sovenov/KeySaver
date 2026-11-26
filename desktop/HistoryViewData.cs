using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeySaver
{
    internal sealed class AppItem
    {
        public string Key { get; }
        public string Name { get; }
        public string Path { get; }
        public Geometry Glyph { get; }
        public ImageSource Icon
        {
            get
            {
                if (!iconChecked)
                {
                    iconChecked = true;
                    icon = AppIcons.Load(Path);
                }
                return icon;
            }
        }
        public bool HasIcon => Icon != null;
        public int Count { get; }
        private ImageSource icon;
        private bool iconChecked;

        public AppItem(string key, string name, string path, Geometry glyph, int count = 0)
        {
            Key = key;
            Name = name;
            Path = path ?? string.Empty;
            Glyph = glyph;
            Count = count;
        }

        public bool Matches(string query)
        {
            return Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                System.IO.Path.GetFileName(Path).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal sealed class SessionView
    {
        internal static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
        private readonly AppItem application;
        private readonly DateTime start;
        private readonly DateTime end;

        public SessionRecord Record { get; }
        private readonly string fullPath;
        private ImageSource icon;
        private bool iconChecked;
        public string AppName => application.Name;
        public string Name => application.Name;
        public Geometry Glyph => application.Glyph;
        public ImageSource Icon
        {
            get
            {
                if (!iconChecked)
                {
                    iconChecked = true;
                    icon = AppIcons.Load(fullPath);
                }
                return icon;
            }
        }
        public bool HasIcon => Icon != null;
        public string Path => string.IsNullOrEmpty(fullPath) ? "—" : fullPath;
        public string Text => Record.Text;
        public string Snippet { get; }
        public DateTime Day => start.Date;
        public string DayText { get; }
        public DateTime Start => start;
        public long GroupOrder { get; }
        public string TimeText => start.ToString("HH:mm", Russian);
        public string StartText => start.ToString("HH:mm:ss", Russian);
        public string EndText => end.ToString("HH:mm:ss", Russian);
        public string DateTimeText => start.ToString("dd.MM.yyyy · HH:mm:ss", Russian);

        public SessionView(SessionRecord record, AppItem application, DateTime today, bool oldestFirst = false)
        {
            Record = record;
            this.application = application;
            fullPath = !string.IsNullOrWhiteSpace(record.Path) ? record.Path : application.Path;
            start = record.StartedAt.LocalDateTime;
            end = record.EndedAt.LocalDateTime;
            GroupOrder = oldestFirst ? start.Date.Ticks : -start.Date.Ticks;
            Snippet = string.Join(" ", record.Text.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
            string date = start.ToString(start.Year == today.Year ? "d MMMM" : "d MMMM yyyy", Russian);
            DayText = start.Date == today ? "Сегодня, " + date :
                start.Date == today.AddDays(-1) ? "Вчера, " + date : date;
        }
    }

    internal static class HistoryCatalog
    {
        private static readonly Geometry grid = Shape("M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z");
        private static readonly AppItem[] knownApps =
        {
            new AppItem("keysaver", "KeySaver", typeof(HistoryCatalog).Assembly.Location,
                Shape("M3,6 H21 V18 H3 Z M7,10 H8 M11,10 H13 M16,10 H17 M7,14 H8 M10,14 H14 M16,14 H17")),
            new AppItem("notepad", "Блокнот", @"C:\Windows\System32\notepad.exe",
                Shape("M6,4 H18 V20 H6 Z M8,2 V6 M12,2 V6 M16,2 V6 M9,10 H15 M9,14 H15 M9,17 H13")),
            new AppItem("code", "Visual Studio Code", @"C:\Users\Alex\AppData\Local\Programs\Microsoft VS Code\Code.exe",
                Shape("M8,7 L3,12 8,17 M16,7 L21,12 16,17 M13,5 L11,19")),
            new AppItem("edge", "Microsoft Edge", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                Shape("M12,4 A8,8 0 1 1 12,20 A8,8 0 1 1 12,4 M15.5,8.5 L13.8,13.8 8.5,15.5 10.2,10.2 Z")),
            new AppItem("outlook", "Outlook", @"C:\Program Files\Microsoft Office\root\Office16\OUTLOOK.EXE",
                Shape("M4,6 H20 V18 H4 Z M4,7 L12,13 20,7"))
        };

        public static IReadOnlyList<AppItem> KnownApps => knownApps;
        public static AppItem AllApps(int count) => new AppItem("all", "Все приложения", string.Empty, grid, count);
        public static AppItem CustomApp() => new AppItem("custom", "Указать путь к .exe", string.Empty, grid);

        public static string KeyForProcess(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "unknown";
            var file = System.IO.Path.GetFileName(path);
            var stem = System.IO.Path.GetFileNameWithoutExtension(file);
            foreach (var item in knownApps)
            {
                var knownFile = System.IO.Path.GetFileName(item.Path);
                if (string.Equals(knownFile, file, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Key, stem, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(System.IO.Path.GetFileNameWithoutExtension(knownFile), stem, StringComparison.OrdinalIgnoreCase))
                    return item.Key;
            }
            if (string.IsNullOrWhiteSpace(stem)) return "unknown";
            stem = stem.Trim();
            return stem.Length <= 128 ? stem : stem.Substring(0, 128);
        }

        public static AppItem Find(string key, int count = 0, string path = null)
        {
            foreach (var item in knownApps)
            {
                if (item.Key == key)
                    return new AppItem(key, item.Name, string.IsNullOrWhiteSpace(path) ? item.Path : path, item.Glyph, count);
            }
            return new AppItem(key, key, path ?? string.Empty, grid, count);
        }

        private static Geometry Shape(string path)
        {
            var geometry = Geometry.Parse(path);
            geometry.Freeze();
            return geometry;
        }
    }

    internal static class AppIcons
    {
        private static readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static ImageSource Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            lock (cache)
            {
                if (cache.TryGetValue(path, out var cached)) return cached;
            }

            var loaded = Extract(path);
            lock (cache) cache[path] = loaded;
            return loaded;
        }

        private static ImageSource Extract(string path)
        {
            var info = new ShellFileInfo();
            var result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), Icon | LargeIcon);
            if (result == IntPtr.Zero || info.Icon == IntPtr.Zero) return null;
            try
            {
                var image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return image;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                DestroyIcon(info.Icon);
            }
        }

        private const uint Icon = 0x100;
        private const uint LargeIcon = 0x0;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShellFileInfo
        {
            public IntPtr Icon;
            public int Index;
            public uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string DisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string TypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr icon);
    }
}
