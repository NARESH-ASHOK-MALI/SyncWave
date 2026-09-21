using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace SyncWave.Utils
{
    /// <summary>
    /// Manages global system-wide hotkey registration and Win32 message handling via HwndSource.
    /// </summary>
    public class HotkeyManager : IDisposable
    {
        private const int HOTKEY_ID = 9001;
        private const int WM_HOTKEY = 0x0312;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private IntPtr _hWnd;
        private HwndSource? _hwndSource;
        private bool _isRegistered;

        public event EventHandler? HotkeyPressed;

        public bool IsRegistered => _isRegistered;
        public string CurrentHotkeyString { get; private set; } = "Ctrl+Shift+Space";

        private static string ConfigFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SyncWave",
            "hotkey_pref.txt");

        public HotkeyManager()
        {
            CurrentHotkeyString = LoadHotkeyConfig();
        }

        public static string LoadHotkeyConfig()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string content = File.ReadAllText(ConfigFilePath).Trim();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        return content;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to load hotkey config, using default: {ex.Message}");
            }
            return "Ctrl+Shift+Space";
        }

        public static bool SaveHotkeyConfig(string hotkeyString)
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigFilePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(ConfigFilePath, hotkeyString);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save hotkey config '{hotkeyString}'", ex);
                return false;
            }
        }

        public bool Register(HwndSource hwndSource)
        {
            if (hwndSource == null) throw new ArgumentNullException(nameof(hwndSource));

            Unregister();

            _hwndSource = hwndSource;
            _hWnd = hwndSource.Handle;

            _hwndSource.AddHook(WndProc);

            return RegisterCurrentHotkey();
        }

        public bool Register(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) throw new ArgumentNullException(nameof(hWnd));

            Unregister();

            _hWnd = hWnd;
            _hwndSource = HwndSource.FromHwnd(hWnd);
            _hwndSource?.AddHook(WndProc);

            return RegisterCurrentHotkey();
        }

        public bool UpdateHotkey(string hotkeyString)
        {
            CurrentHotkeyString = hotkeyString;
            SaveHotkeyConfig(hotkeyString);

            if (_hWnd != IntPtr.Zero)
            {
                return RegisterCurrentHotkey();
            }
            return true;
        }

        private bool RegisterCurrentHotkey()
        {
            if (_hWnd == IntPtr.Zero) return false;

            if (_isRegistered)
            {
                UnregisterHotKey(_hWnd, HOTKEY_ID);
                _isRegistered = false;
            }

            if (!ParseHotkeyString(CurrentHotkeyString, out uint modifiers, out uint vk))
            {
                Logger.Warn($"Could not parse hotkey string '{CurrentHotkeyString}'. Falling back to Ctrl+Shift+Space.");
                CurrentHotkeyString = "Ctrl+Shift+Space";
                ParseHotkeyString(CurrentHotkeyString, out modifiers, out vk);
            }

            // Include MOD_NOREPEAT to prevent rapid repetitive triggers when holding down key combination
            modifiers |= MOD_NOREPEAT;

            bool success = RegisterHotKey(_hWnd, HOTKEY_ID, modifiers, vk);
            if (success)
            {
                _isRegistered = true;
                Logger.Info($"Successfully registered global hotkey: {CurrentHotkeyString}");
            }
            else
            {
                int error = Marshal.GetLastWin32Error();
                Logger.Warn($"Failed to register global hotkey '{CurrentHotkeyString}' (Win32 Error: {error}). The hotkey combination may be claimed by another application.");
            }

            return success;
        }

        public void Unregister()
        {
            if (_isRegistered && _hWnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hWnd, HOTKEY_ID);
                _isRegistered = false;
            }

            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }

            _hWnd = IntPtr.Zero;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                Logger.Info($"Global hotkey '{CurrentHotkeyString}' triggered.");
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
                handled = true;
            }
            return IntPtr.Zero;
        }

        public static bool ParseHotkeyString(string hotkeyString, out uint modifiers, out uint vk)
        {
            modifiers = 0;
            vk = 0;

            if (string.IsNullOrWhiteSpace(hotkeyString)) return false;

            string[] parts = hotkeyString.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return false;

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (i < parts.Length - 1)
                {
                    // Modifier part
                    switch (part.ToLowerInvariant())
                    {
                        case "ctrl":
                        case "control":
                            modifiers |= MOD_CONTROL;
                            break;
                        case "shift":
                            modifiers |= MOD_SHIFT;
                            break;
                        case "alt":
                            modifiers |= MOD_ALT;
                            break;
                        case "win":
                        case "windows":
                            modifiers |= MOD_WIN;
                            break;
                        default:
                            return false;
                    }
                }
                else
                {
                    // Key part
                    if (Enum.TryParse<Key>(part, true, out Key key))
                    {
                        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    }
                    else if (part.Equals("Space", StringComparison.OrdinalIgnoreCase))
                    {
                        vk = (uint)KeyInterop.VirtualKeyFromKey(Key.Space);
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return vk != 0;
        }

        public void Dispose()
        {
            Unregister();
            GC.SuppressFinalize(this);
        }
    }
}
