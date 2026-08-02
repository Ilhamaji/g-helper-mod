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

        private static int _lastMatchedBoostMode = -1;
        private static string _lastMatchedApp = string.Empty;
        private static int _lastMatchedPid = 0;

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

        public static bool IsAltTabProtectionEnabled
        {
            get => AppConfig.Is("app_auto_boost_alt_tab_protection", 1);
            set => AppConfig.Set("app_auto_boost_alt_tab_protection", value ? 1 : 0);
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
            _lastMatchedApp = string.Empty;
            _lastMatchedBoostMode = -1;
            _lastMatchedPid = 0;
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
            _lastMatchedApp = string.Empty;
            _lastMatchedBoostMode = -1;
            _lastMatchedPid = 0;
            Logger.WriteLine("AppAutoBoost service stopped.");
        }

        private static void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (eventType != EVENT_SYSTEM_FOREGROUND) return;
            Task.Run(() => CheckActiveForegroundApp());
        }

        private static bool IsProcessStillRunning(int pid)
        {
            if (pid <= 0) return false;
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById(pid);
                return !p.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryFindRunningTargetApp(out string appName, out int boostMode, out int pid)
        {
            appName = string.Empty;
            boostMode = -1;
            pid = 0;

            if (_lastMatchedPid > 0 && IsProcessStillRunning(_lastMatchedPid))
            {
                appName = _lastMatchedApp;
                boostMode = _lastMatchedBoostMode;
                pid = _lastMatchedPid;
                return true;
            }

            lock (_ruleLock)
            {
                if (_rules == null || _rules.Count == 0) return false;

                var runningProcesses = System.Diagnostics.Process.GetProcesses();
                try
                {
                    foreach (var rule in _rules)
                    {
                        foreach (var proc in runningProcesses)
                        {
                            try
                            {
                                if (proc.ProcessName.Equals(rule.ProcessName, StringComparison.OrdinalIgnoreCase))
                                {
                                    appName = rule.ProcessName;
                                    boostMode = rule.BoostMode;
                                    pid = proc.Id;
                                    return true;
                                }
                            }
                            catch { }
                        }
                    }
                }
                finally
                {
                    foreach (var p in runningProcesses) p.Dispose();
                }
            }

            return false;
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

                _lastMatchedApp = matchedRule.ProcessName;
                _lastMatchedBoostMode = matchedRule.BoostMode;
                _lastMatchedPid = (int)pid;
            }
            else
            {
                if (_defaultBoostMode != -1)
                {
                    if (IsAltTabProtectionEnabled && TryFindRunningTargetApp(out string bgApp, out int bgMode, out int bgPid))
                    {
                        if (_lastAppliedBoostMode != bgMode)
                        {
                            PowerNative.SetCPUBoost(bgMode);
                            _lastAppliedBoostMode = bgMode;
                        }
                        Logger.WriteLine($"AppAutoBoost Alt+Tab protection active: Keeping CPU Boost at mode {bgMode} for background app '{bgApp}' (PID {bgPid})");
                    }
                    else
                    {
                        PowerNative.SetCPUBoost(_defaultBoostMode);
                        Logger.WriteLine($"AppAutoBoost unfocused target app: Restored CPU Boost to mode {_defaultBoostMode}");
                        _lastAppliedBoostMode = -1;
                        _defaultBoostMode = -1;
                        _lastMatchedApp = string.Empty;
                        _lastMatchedBoostMode = -1;
                        _lastMatchedPid = 0;
                    }
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
