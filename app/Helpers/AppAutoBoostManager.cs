using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GHelper.Mode;

namespace GHelper.Helpers
{
    public class TargetAppRule
    {
        public string ProcessName { get; set; } = string.Empty;
        public int BoostMode { get; set; } = 2; // Default Aggressive
        public string ExePath { get; set; } = string.Empty;
    }

    public static class AppAutoBoostManager
    {
        // ── Win32 P/Invoke ───────────────────────────────────────────────────
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

        private const uint WINEVENT_OUTOFCONTEXT = 0;
        private const uint EVENT_SYSTEM_FOREGROUND = 3;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        // ── Fields ────────────────────────────────────────────────────────────
        private static WinEventDelegate? _winEventDelegate;
        private static IntPtr _hookHandle = IntPtr.Zero;
        private static bool _isServiceRunning = false;
        private static string _currentActiveApp = string.Empty;
        private static int _defaultBoostMode = -1;
        private static int _lastAppliedBoostMode = -1;

        private static List<TargetAppRule> _rules = new();
        private static readonly object _ruleLock = new();

        public static bool IsEnabled
        {
            get => AppConfig.Is("app_auto_boost_enabled");
            set
            {
                AppConfig.Set("app_auto_boost_enabled", value ? 1 : 0);
                if (value) StartService();
                else StopService();
            }
        }

        public static List<TargetAppRule> GetRules()
        {
            lock (_ruleLock)
            {
                return new List<TargetAppRule>(_rules);
            }
        }

        public static void SaveRules(List<TargetAppRule> newRules)
        {
            lock (_ruleLock)
            {
                _rules = new List<TargetAppRule>(newRules);
                try
                {
                    string json = JsonSerializer.Serialize(_rules);
                    AppConfig.Set("target_app_rules", json);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Failed to save target_app_rules: " + ex.Message);
                }
            }
            if (_isServiceRunning) CheckActiveForegroundApp();
        }

        public static void Initialize()
        {
            LoadRules();
            if (IsEnabled) StartService();
        }

        private static void LoadRules()
        {
            lock (_ruleLock)
            {
                _rules.Clear();
                string? json = AppConfig.GetString("target_app_rules");
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        var loaded = JsonSerializer.Deserialize<List<TargetAppRule>>(json);
                        if (loaded != null) _rules = loaded;
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine("Failed to load target_app_rules: " + ex.Message);
                    }
                }
            }
        }

        public static void StartService()
        {
            if (_isServiceRunning) return;

            _winEventDelegate = new WinEventDelegate(WinEventProc);
            _hookHandle = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _winEventDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
            _isServiceRunning = true;
            Logger.WriteLine("AppAutoBoost service started.");
            CheckActiveForegroundApp();
        }

        public static void ResetDefaultBoost()
        {
            _defaultBoostMode = -1;
            _lastAppliedBoostMode = -1;
        }

        public static void StopService()
        {
            if (!_isServiceRunning) return;

            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWinEvent(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
            _winEventDelegate = null;
            _isServiceRunning = false;

            if (_defaultBoostMode != -1)
            {
                PowerNative.SetCPUBoost(_defaultBoostMode);
                _defaultBoostMode = -1;
            }
            _lastAppliedBoostMode = -1;
            Logger.WriteLine("AppAutoBoost service stopped.");
        }

        private static void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (eventType != EVENT_SYSTEM_FOREGROUND) return;
            Task.Run(() => CheckActiveForegroundApp());
        }

        private static void CheckActiveForegroundApp()
        {
            if (!_isServiceRunning) return;

            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd)) return;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return;

            string procName = GetProcessNameFromPid(pid);
            if (string.IsNullOrEmpty(procName)) return;

            if (procName.Equals(_currentActiveApp, StringComparison.OrdinalIgnoreCase)) return;
            _currentActiveApp = procName;

            TargetAppRule? matchedRule = null;
            lock (_ruleLock)
            {
                foreach (var rule in _rules)
                {
                    if (procName.Equals(rule.ProcessName, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedRule = rule;
                        break;
                    }
                }
            }

            if (matchedRule != null)
            {
                if (_defaultBoostMode == -1)
                {
                    _defaultBoostMode = PowerNative.GetCPUBoost();
                }

                if (_lastAppliedBoostMode != matchedRule.BoostMode)
                {
                    PowerNative.SetCPUBoost(matchedRule.BoostMode);
                    _lastAppliedBoostMode = matchedRule.BoostMode;
                    Logger.WriteLine($"AppAutoBoost matched '{procName}': Switched CPU Boost to mode {matchedRule.BoostMode}");
                }
            }
            else
            {
                if (_defaultBoostMode != -1)
                {
                    PowerNative.SetCPUBoost(_defaultBoostMode);
                    Logger.WriteLine($"AppAutoBoost unfocused target app: Restored CPU Boost to mode {_defaultBoostMode}");
                    _lastAppliedBoostMode = -1;
                    _defaultBoostMode = -1;
                }
            }
        }

        private static string GetProcessNameFromPid(uint pid)
        {
            IntPtr hProcess = IntPtr.Zero;
            try
            {
                hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProcess != IntPtr.Zero)
                {
                    StringBuilder sb = new StringBuilder(512);
                    uint size = (uint)sb.Capacity;
                    if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                    {
                        return Path.GetFileNameWithoutExtension(sb.ToString());
                    }
                }
            }
            catch { }
            finally
            {
                if (hProcess != IntPtr.Zero) CloseHandle(hProcess);
            }
            return string.Empty;
        }
    }
}
