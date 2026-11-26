using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace KeySaver
{
    public sealed class KeyboardRecorder : IDisposable
    {
        private readonly Action<SessionRecord> onSession;
        private readonly object gate = new object();
        private readonly ManualResetEventSlim ready = new ManualResetEventSlim(false);
        private readonly uint selfPid = (uint)Process.GetCurrentProcess().Id;
        private readonly StringBuilder buffer = new StringBuilder();
        private LowLevelProc keyboardProc;
        private LowLevelProc mouseProc;
        private Thread thread;
        private System.Threading.Timer idleTimer;
        private IntPtr keyboardHook;
        private IntPtr mouseHook;
        private uint threadId;
        private bool running;
        private bool stop;
        private string startError;
        private bool mouseRule = true;
        private bool tabRule = true;
        private bool enterRule;
        private bool idleRule = true;
        private int idleSeconds = 10;
        private string mode = "all";
        private string[] onlyPaths = Array.Empty<string>();
        private string[] exceptPaths = Array.Empty<string>();
        private bool started;
        private DateTimeOffset startedAt;
        private string app = "unknown";
        private string appPath;
        private IntPtr seenWindow;
        private string seenPath;
        private string seenKey = "unknown";
        private bool seenSelf;

        public KeyboardRecorder(Action<SessionRecord> onSession)
        {
            this.onSession = onSession ?? throw new ArgumentNullException(nameof(onSession));
            keyboardProc = KeyboardCallback;
            mouseProc = MouseCallback;
        }

        public string Start()
        {
            if (thread != null) return startError;
            stop = false;
            startError = null;
            ready.Reset();
            thread = new Thread(Loop) { IsBackground = true, Name = "KeySaver.Input" };
            thread.Start();
            if (!ready.Wait(5000))
                startError = "Не удалось начать запись ввода с клавиатуры.";
            if (startError != null)
            {
                thread.Join(2000);
                thread = null;
            }
            return startError;
        }

        public SessionRecord Stop()
        {
            SessionRecord pending;
            uint id;
            lock (gate)
            {
                stop = true;
                running = false;
                pending = Detach();
                id = threadId;
            }
            if (thread == null) return pending;
            if (id != 0) PostThreadMessage(id, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            thread.Join(2000);
            thread = null;
            threadId = 0;
            return pending;
        }

        public SessionRecord Apply(Preferences preferences)
        {
            if (preferences == null) throw new ArgumentNullException(nameof(preferences));
            SessionRecord closed = null;
            lock (gate)
            {
                mouseRule = preferences.MouseRule;
                tabRule = preferences.TabRule;
                enterRule = preferences.EnterRule;
                idleRule = preferences.IdleRule;
                idleSeconds = preferences.IdleSeconds < 1 ? 10 : preferences.IdleSeconds;
                mode = preferences.Mode ?? "all";
                onlyPaths = Copy(preferences.OnlyPaths);
                exceptPaths = Copy(preferences.ExceptPaths);
                if (started && !Allowed(seenSelf ? null : seenPath)) closed = Detach();
                else if (started && idleRule) Arm();
                else Disarm();
            }
            return closed;
        }

        public void Dispose()
        {
            Stop();
            idleTimer?.Dispose();
            idleTimer = null;
            ready.Dispose();
        }

        private void Loop()
        {
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            lock (gate)
            {
                threadId = GetCurrentThreadId();
                if (!stop) running = true;
            }
            if (running && !Install())
            {
                startError = "Не удалось начать запись ввода с клавиатуры.";
                RemoveHooks();
                lock (gate) running = false;
            }
            ready.Set();
            if (!running) return;
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
                DispatchMessage(ref message);
            RemoveHooks();
            lock (gate) running = false;
        }

        private bool Install()
        {
            keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, GetModuleHandle(null), 0);
            mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, GetModuleHandle(null), 0);
            return keyboardHook != IntPtr.Zero && mouseHook != IntPtr.Zero;
        }

        private void RemoveHooks()
        {
            if (keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(keyboardHook);
                keyboardHook = IntPtr.Zero;
            }
            if (mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(mouseHook);
                mouseHook = IntPtr.Zero;
            }
        }

        private IntPtr KeyboardCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                try { OnKey(Marshal.PtrToStructure<Kbdll>(lParam)); }
                catch (Exception) { }
            }
            return CallNextHookEx(keyboardHook, code, wParam, lParam);
        }

        private IntPtr MouseCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && (wParam == (IntPtr)WM_LBUTTONDOWN || wParam == (IntPtr)WM_NCLBUTTONDOWN))
            {
                try { OnLeftClick(); }
                catch (Exception) { }
            }
            return CallNextHookEx(mouseHook, code, wParam, lParam);
        }

        private void OnLeftClick()
        {
            SessionRecord done;
            lock (gate)
            {
                if (!running || !mouseRule) return;
                done = Detach();
            }
            Publish(done);
        }

        private void OnKey(Kbdll keyData)
        {
            SessionRecord done = null;
            lock (gate)
            {
                if (running)
                {
                    var key = (Keys)keyData.vkCode;
                    var target = LookAtForeground();
                    if (started && (target.Skip || !string.Equals(target.Key, app, StringComparison.Ordinal)))
                        done = Detach();
                    if (!target.Skip) TakeKey(key, keyData, target, ref done);
                }
            }
            Publish(done);
        }

        private void TakeKey(Keys key, Kbdll keyData, Target target, ref SessionRecord done)
        {
            if (IsModifier(key))
            {
                Arm();
                return;
            }

            bool ctrl, alt, win;
            Modifiers(out ctrl, out alt, out win);
            bool plain = !ctrl && !alt && !win;
            if (key == Keys.Tab)
            {
                if (tabRule && plain) done = done ?? Detach();
                else if (plain) Type("\t", target);
                else Arm();
            }
            else if (key == Keys.Return)
            {
                if (enterRule && plain) done = done ?? Detach();
                else if (plain) Type("\n", target);
                else Arm();
            }
            else if (key == Keys.Back && !alt && !win)
            {
                if (started) Backspace(ctrl);
                if (buffer.Length == 0) { started = false; Disarm(); }
                else Arm();
            }
            else if ((ctrl && !alt) || (alt && !ctrl) || win)
            {
                Arm();
            }
            else
            {
                var text = Translate(keyData.vkCode, keyData.scanCode, keyData.flags, target.Layout);
                if (!string.IsNullOrEmpty(text)) Type(text, target);
                else Arm();
            }
        }

        private void Type(string text, Target target)
        {
            if (!started)
            {
                started = true;
                startedAt = DateTimeOffset.UtcNow;
                app = string.IsNullOrEmpty(target.Key) ? "unknown" : target.Key;
                appPath = target.Path;
            }
            buffer.Append(text);
            Arm();
        }

        private void Backspace(bool word)
        {
            if (buffer.Length == 0) return;
            if (!word)
            {
                buffer.Length -= char.IsLowSurrogate(buffer[buffer.Length - 1]) && buffer.Length >= 2 ? 2 : 1;
                return;
            }
            int index = buffer.Length - 1;
            while (index >= 0 && char.IsWhiteSpace(buffer[index])) index--;
            while (index >= 0 && !char.IsWhiteSpace(buffer[index])) index--;
            buffer.Length = index + 1;
        }

        private void OnIdle(object state)
        {
            SessionRecord done;
            lock (gate)
            {
                if (!running) return;
                done = Detach();
            }
            Publish(done);
        }

        private void Arm()
        {
            if (!started || !idleRule) return;
            if (idleTimer == null) idleTimer = new System.Threading.Timer(OnIdle, null, Timeout.Infinite, Timeout.Infinite);
            idleTimer.Change(idleSeconds * 1000, Timeout.Infinite);
        }

        private void Disarm()
        {
            idleTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private SessionRecord Detach()
        {
            Disarm();
            if (!started || buffer.Length == 0)
            {
                started = false;
                buffer.Clear();
                return null;
            }
            var session = new SessionRecord(app, startedAt, DateTimeOffset.UtcNow, buffer.ToString(), appPath);
            buffer.Clear();
            started = false;
            appPath = null;
            return session;
        }

        private void Publish(SessionRecord session)
        {
            if (session == null) return;
            try { onSession(session); }
            catch (Exception) { }
        }

        private Target LookAtForeground()
        {
            var hwnd = GetForegroundWindow();
            uint pid;
            var threadIdOfWindow = GetWindowThreadProcessId(hwnd, out pid);
            var layout = GetKeyboardLayout(threadIdOfWindow);
            if (hwnd != seenWindow)
            {
                seenWindow = hwnd;
                seenSelf = pid == 0 || pid == selfPid;
                seenPath = seenSelf ? null : QueryPath(pid);
                seenKey = HistoryCatalog.KeyForProcess(seenPath);
            }
            return new Target
            {
                Skip = seenSelf || !Allowed(seenPath),
                Key = seenKey,
                Path = Rooted(seenPath),
                Layout = layout
            };
        }

        private bool Allowed(string path)
        {
            if (mode == "only") return Listed(onlyPaths, path);
            if (mode == "except") return !Listed(exceptPaths, path);
            return true;
        }

        private static bool Listed(string[] paths, string path)
        {
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(path)) return false;
            var file = System.IO.Path.GetFileName(path);
            foreach (var entry in paths)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                if (string.Equals(entry, path, StringComparison.OrdinalIgnoreCase)) return true;
                var name = System.IO.Path.GetFileName(entry.Trim().Trim('"'));
                if (string.Equals(name, file, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(name), file, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string QueryPath(uint pid)
        {
            var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    var text = new StringBuilder(1024);
                    int size = text.Capacity;
                    if (QueryFullProcessImageName(handle, 0, text, ref size)) return text.ToString();
                }
                finally { CloseHandle(handle); }
            }
            try
            {
                using (var process = Process.GetProcessById((int)pid))
                    return process.ProcessName;
            }
            catch (Exception) { return null; }
        }

        private static string[] Copy(System.Collections.Generic.List<string> paths)
        {
            return paths == null || paths.Count == 0 ? Array.Empty<string>() : paths.ToArray();
        }

        private static void Modifiers(out bool ctrl, out bool alt, out bool win)
        {
            ctrl = Down(Keys.ControlKey) || Down(Keys.LControlKey) || Down(Keys.RControlKey);
            alt = Down(Keys.Menu) || Down(Keys.LMenu) || Down(Keys.RMenu);
            win = Down(Keys.LWin) || Down(Keys.RWin);
        }

        private static bool Down(Keys key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

        private static bool IsModifier(Keys key)
        {
            switch (key)
            {
                case Keys.ShiftKey:
                case Keys.LShiftKey:
                case Keys.RShiftKey:
                case Keys.ControlKey:
                case Keys.LControlKey:
                case Keys.RControlKey:
                case Keys.Menu:
                case Keys.LMenu:
                case Keys.RMenu:
                case Keys.LWin:
                case Keys.RWin:
                case Keys.Capital:
                case Keys.NumLock:
                case Keys.Scroll:
                    return true;
                default:
                    return false;
            }
        }

        private static string Translate(uint vk, uint scan, uint flags, IntPtr layout)
        {
            if (vk == 0xE7)
            {
                var character = (char)scan;
                return char.IsControl(character) ? null : character.ToString();
            }
            var state = new byte[256];
            for (int index = 0; index < state.Length; index++)
                if ((GetAsyncKeyState(index) & 0x8000) != 0) state[index] = 0x80;
            if (vk < state.Length) state[vk] |= 0x80;
            state[0x14] |= (byte)(GetKeyState(0x14) & 1);
            state[0x90] |= (byte)(GetKeyState(0x90) & 1);
            state[0x91] |= (byte)(GetKeyState(0x91) & 1);
            if ((flags & 1) != 0) scan |= 0xE000;
            var text = new char[8];
            int count = ToUnicodeEx(vk, scan, state, text, text.Length, 0, layout);
            if (count <= 0) return null;
            var value = new string(text, 0, count);
            return value.Length == 1 && char.IsControl(value[0]) ? null : value;
        }

        private static string Rooted(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && System.IO.Path.IsPathRooted(path) ? path : null;
        }

        private struct Target
        {
            public bool Skip;
            public string Key;
            public string Path;
            public IntPtr Layout;
        }

        private delegate IntPtr LowLevelProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct Kbdll
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr extra;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Msg
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int x;
            public int y;
        }

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_QUIT = 0x0012;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc proc, IntPtr module, uint thread);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint threadId);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int key);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, [In, Out] char[] buffer, int capacity, uint flags, IntPtr layout);

        [DllImport("user32.dll")]
        private static extern int PeekMessage(out Msg message, IntPtr hwnd, uint min, uint max, uint remove);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out Msg message, IntPtr hwnd, uint min, uint max);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref Msg message);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
