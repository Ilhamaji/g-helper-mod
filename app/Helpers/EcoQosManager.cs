using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace GHelper.Helpers
{
    public class EcoQosRule
    {
        public string ProcessName { get; set; } = string.Empty;
        public bool EcoEnabled { get; set; } = true;
        public string ExePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// EcoQoS Manager — applies Windows Energy-based Quality of Service
    /// (EcoQoS / efficient throttling) to target applications so they run
    /// with reduced power and heat.
    ///
    /// Two schemes are supported:
    ///  - Per-application rules (auto-applied while the process runs).
    ///  - Global/preset mode: whenever the global toggle is on, every process
    ///    in the global list is forced to EcoQoS (and optionally only while a
    ///    foreground "gaming/performance" app is active).
    /// </summary>
    public static class EcoQosManager
    {
        // ── Win32 P/Invoke ────────────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessInformation(IntPtr hProcess, int ProcessInformationClass, ref PROCESS_POWER_THROTTLING_STATE ProcessInformation, uint ProcessInformationSize);

        // ── Constants ────────────────────────────────────────────────────────
        private const uint PROCESS_SET_INFORMATION = 0x0200;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        private const int ProcessPowerThrottling = 3; // ProcessInformationClass
        private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
        private const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x4;

        // ── State ────────────────────────────────────────────────────────────
        private static readonly object _ruleLock = new();
        private static readonly object _stateLock = new();
        private static List<EcoQosRule> _rules = new();
        private static readonly HashSet<string> _globalProcesses = new(StringComparer.OrdinalIgnoreCase);
        private static System.Threading.Timer? _monitorTimer;
        private static bool _isRunning;
        private static bool _isSupported = true;

        public static bool IsEnabled
        {
            get => AppConfig.IsNotFalse("ecoqos_enabled");
            set
            {
                AppConfig.Set("ecoqos_enabled", value ? 1 : 0);
                if (value) Start();
                else Stop();
            }
        }

        public static bool IsGlobalEnabled
        {
            get => AppConfig.Is("ecoqos_global_enabled");
            set => AppConfig.Set("ecoqos_global_enabled", value ? 1 : 0);
        }

        // When global + game mode are on, EcoQoS is only applied to the global
        // list while a "performance" style app is in the foreground.
        public static bool IsGameModeEnabled
        {
            get => AppConfig.IsNotFalse("ecoqos_game_mode");
            set => AppConfig.Set("ecoqos_game_mode", value ? 1 : 0);
        }

        /// <summary>False when the running Windows does not support process power throttling (EcoQoS).</summary>
        public static bool IsSupported
        {
            get { lock (_stateLock) return _isSupported; }
        }

        /// <summary>True when EcoQoS semantics are available on this OS.</summary>
        public static bool IsEcoQoSAvailable
        {
            get
            {
                try
                {
                    return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 17763;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static List<EcoQosRule> GetRules()
        {
            lock (_ruleLock)
            {
                return new List<EcoQosRule>(_rules);
            }
        }

        public static void SaveRules(List<EcoQosRule> newRules)
        {
            lock (_ruleLock)
            {
                _rules = new List<EcoQosRule>(newRules);
                try
                {
                    string json = JsonSerializer.Serialize(_rules);
                    AppConfig.Set("ecoqos_rules", json);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Failed to save ecoqos_rules: " + ex.Message);
                }
            }
        }

        public static void LoadRules()
        {
            lock (_ruleLock)
            {
                _rules.Clear();
                string? json = AppConfig.GetString("ecoqos_rules");
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        var loaded = JsonSerializer.Deserialize<List<EcoQosRule>>(json);
                        if (loaded != null) _rules = loaded;
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine("Failed to load ecoqos_rules: " + ex.Message);
                    }
                }
            }
        }

        public static List<string> GetGlobalProcesses()
        {
            lock (_stateLock)
            {
                return new List<string>(_globalProcesses);
            }
        }

        public static void SaveGlobalProcesses(List<string> processes)
        {
            lock (_stateLock)
            {
                _globalProcesses.Clear();
                foreach (var p in processes)
                {
                    if (!string.IsNullOrWhiteSpace(p))
                        _globalProcesses.Add(p.Trim());
                }
                try
                {
                    string json = JsonSerializer.Serialize(new List<string>(_globalProcesses));
                    AppConfig.Set("ecoqos_global_processes", json);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Failed to save ecoqos_global_processes: " + ex.Message);
                }
            }
        }

        private static void LoadGlobalProcesses()
        {
            lock (_stateLock)
            {
                _globalProcesses.Clear();
                string? json = AppConfig.GetString("ecoqos_global_processes");
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        var loaded = JsonSerializer.Deserialize<List<string>>(json);
                        if (loaded != null)
                        {
                            foreach (var p in loaded)
                            {
                                if (!string.IsNullOrWhiteSpace(p))
                                    _globalProcesses.Add(p.Trim());
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine("Failed to load ecoqos_global_processes: " + ex.Message);
                    }
                }
            }
        }

        public static void Initialize()
        {
            LoadRules();
            LoadGlobalProcesses();
            if (IsEnabled) Start();
        }

        public static void Start()
        {
            lock (_stateLock)
            {
                if (_isRunning) return;
                _isRunning = true;
                _monitorTimer?.Dispose();
                _monitorTimer = new System.Threading.Timer(OnMonitorTick, null, 1000, 2000);
                Logger.WriteLine("EcoQosManager started.");
            }
            ApplyAllRules();
        }

        public static void Stop()
        {
            lock (_stateLock)
            {
                if (!_isRunning) return;
                _isRunning = false;
                _monitorTimer?.Dispose();
                _monitorTimer = null;
            }
            Logger.WriteLine("EcoQosManager stopped.");
        }

        /// <summary>Set EcoQoS (efficient throttling) for a process by pid.</summary>
        public static bool SetEco(int pid, bool enable)
        {
            if (!IsEcoQoSAvailable) return false;
            if (pid <= 0) return false;

            IntPtr hProcess = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_INFORMATION, false, (uint)pid);
            if (hProcess == IntPtr.Zero) return false;

            try
            {
                var state = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                    // Apply the execution-speed throttle. We also set the timer
                    // resolution bit for a "full" EcoQoS experience but keep it
                    // opt-in only via the same mask.
                    ControlMask = enable
                        ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION
                        : PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                    StateMask = enable
                        ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION
                        : 0
                };

                int size = Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>();
                bool ok = SetProcessInformation(hProcess, ProcessPowerThrottling, ref state, (uint)size);

                if (!ok && enable)
                {
                    // Fall back to execution-speed only (some builds reject timer
                    // resolution control without extra privileges).
                    state.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
                    state.StateMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
                    ok = SetProcessInformation(hProcess, ProcessPowerThrottling, ref state, (uint)size);
                }

                return ok;
            }
            catch
            {
                lock (_stateLock) _isSupported = false;
                return false;
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }

        /// <summary>Apply EcoQoS to every running instance of a process name.</summary>
        public static int ApplyToProcess(string processName, bool enable)
        {
            int applied = 0;
            if (string.IsNullOrWhiteSpace(processName)) return 0;

            var procs = System.Diagnostics.Process.GetProcessesByName(processName);
            try
            {
                foreach (var p in procs)
                {
                    try
                    {
                        if (SetEco(p.Id, enable)) applied++;
                    }
                    catch { }
                }
            }
            finally
            {
                foreach (var p in procs) p.Dispose();
            }

            if (applied > 0)
                Logger.WriteLine($"EcoQos: set {processName} EcoQoS={(enable ? "ON" : "OFF")} ({applied} instances)");
            return applied;
        }

        private static void ApplyAllRules()
        {
            List<EcoQosRule> rules;
            lock (_ruleLock) rules = new List<EcoQosRule>(_rules);

            foreach (var rule in rules)
            {
                if (string.IsNullOrWhiteSpace(rule.ProcessName)) continue;
                ApplyToProcess(rule.ProcessName, rule.EcoEnabled);
            }

            ApplyGlobalList();
        }

        // Returns true when a real (non-shell) application window is in the
        // foreground — used by "Game mode" to gate the global EcoQoS list so we
        // don't throttle background work while the user is on the desktop.
        private static bool IsPerformanceAppActive()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return false;

            string name = GetProcessName(pid);
            if (string.IsNullOrEmpty(name)) return true;

            return !IsShellProcess(name);
        }

        private static readonly HashSet<string> _shellProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "shell", "searchapp", "taskmgr", "startmenuexperiencehost",
            "textinputhost", "lockapp", "dwm", "logonui", "systemsettings"
        };

        private static bool IsShellProcess(string name) => _shellProcesses.Contains(name);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref uint lpdwSize);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private static string GetProcessName(uint pid)
        {
            IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess == IntPtr.Zero) return string.Empty;
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder(512);
                uint size = (uint)sb.Capacity;
                if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                {
                    string path = sb.ToString();
                    return System.IO.Path.GetFileNameWithoutExtension(path);
                }
            }
            catch { }
            finally
            {
                CloseHandle(hProcess);
            }
            return string.Empty;
        }

        private static void ApplyGlobalList()
        {
            bool enabled = IsEnabled && IsGlobalEnabled;
            if (!enabled) return;

            List<string> list;
            lock (_stateLock) list = new List<string>(_globalProcesses);

            // In game mode we only apply EcoQoS to the global list when there is
            // a foreground app (assumed active). Otherwise always apply.
            if (IsGameModeEnabled && !IsPerformanceAppActive()) return;

            foreach (var name in list)
            {
                ApplyToProcess(name, true);
            }
        }

        private static void OnMonitorTick(object? state)
        {
            if (!IsEnabled) return;
            ApplyAllRules();
        }
    }
}
