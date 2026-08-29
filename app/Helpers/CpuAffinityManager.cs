using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace GHelper.Helpers
{
    public class AffinityRule
    {
        public string ProcessName { get; set; } = string.Empty;
        // 0 = All cores, 1 = Performance (P) cores only, 2 = Efficiency (E) cores only, 3 = Custom mask
        public int AffinityMode { get; set; } = 0;
        public string CustomMask { get; set; } = string.Empty;
        public string ExePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// CPU Core Affinity Manager — binds target applications to specific CPU
    /// cores (all, P-cores only, E-cores only, or a custom affinity mask).
    /// Rules are applied automatically while the process runs.
    /// </summary>
    public static class CpuAffinityManager
    {
        // ── Win32 P/Invoke ────────────────────────────────────────────────────
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);

        // ── Constants ────────────────────────────────────────────────────────
        private const uint PROCESS_SET_INFORMATION = 0x0200;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        public const int MODE_ALL = 0;
        public const int MODE_PCORES = 1;
        public const int MODE_ECORES = 2;
        public const int MODE_CUSTOM = 3;

        // ── State ────────────────────────────────────────────────────────────
        private static readonly object _ruleLock = new();
        private static readonly object _stateLock = new();
        private static List<AffinityRule> _rules = new();
        private static System.Threading.Timer? _monitorTimer;
        private static bool _isRunning;
        private static ulong _allCoresMask;
        private static ulong _pCoresMask;
        private static ulong _eCoresMask;
        private static bool _topologyDetected;

        private static readonly Dictionary<int, int> _appliedPids = new();

        public static bool IsEnabled
        {
            get => AppConfig.IsNotFalse("cpu_affinity_enabled");
            set
            {
                AppConfig.Set("cpu_affinity_enabled", value ? 1 : 0);
                if (value) Start();
                else Stop();
            }
        }

        /// <summary>Total number of logical processors.</summary>
        public static int ProcessorCount => Environment.ProcessorCount;

        public static bool IsHybridTopologyDetected
        {
            get { lock (_stateLock) return _topologyDetected; }
        }

        public static ulong AllCoresMask
        {
            get { lock (_stateLock) return _allCoresMask; }
        }

        public static ulong PCoresMask
        {
            get { lock (_stateLock) return _pCoresMask; }
        }

        public static ulong ECoresMask
        {
            get { lock (_stateLock) return _eCoresMask; }
        }

        public static List<AffinityRule> GetRules()
        {
            lock (_ruleLock)
            {
                return new List<AffinityRule>(_rules);
            }
        }

        public static void SaveRules(List<AffinityRule> newRules)
        {
            lock (_ruleLock)
            {
                _rules = new List<AffinityRule>(newRules);
                try
                {
                    string json = JsonSerializer.Serialize(_rules);
                    AppConfig.Set("cpu_affinity_rules", json);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Failed to save cpu_affinity_rules: " + ex.Message);
                }
            }
            if (_isRunning) ApplyAllRules();
        }

        public static void LoadRules()
        {
            lock (_ruleLock)
            {
                _rules.Clear();
                string? json = AppConfig.GetString("cpu_affinity_rules");
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        var loaded = JsonSerializer.Deserialize<List<AffinityRule>>(json);
                        if (loaded != null) _rules = loaded;
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine("Failed to load cpu_affinity_rules: " + ex.Message);
                    }
                }
            }
        }

        public static void Initialize()
        {
            DetectTopology();
            LoadRules();
            if (IsEnabled) Start();
        }

        /// <summary>
        /// Detect the CPU core topology so we can build P-core / E-core
        /// affinity masks. Falls back to a flat "all cores" mask when a
        /// hybrid topology cannot be identified.
        /// </summary>
        private static void DetectTopology()
        {
            lock (_stateLock)
            {
                int count = ProcessorCount;
                if (count < 1) count = 1;

                _allCoresMask = (count >= 64) ? ulong.MaxValue : ((1UL << count) - 1);

                int eCores = -1, pCores = -1;
                try { (eCores, pCores) = Program.acpi.GetCores(); } catch { }

                // Hybrid CPU: P-cores are typically the low logical id range on
                // ASUS Intel hybrids; E-cores follow. If detection fails we treat
                // all cores as "all" and disable the P/E-only presets.
                if (eCores > 0 && pCores > 0 && eCores + pCores <= count)
                {
                    // Assume P-cores are processor ids 0..pCores-1 (common ASUS layout).
                    int pCoreIdCount = Math.Min(pCores, count);
                    int eStart = pCoreIdCount;
                    int eCoreIdCount = Math.Min(eCores, count - eStart);

                    _pCoresMask = 0;
                    for (int i = 0; i < pCoreIdCount; i++) _pCoresMask |= (1UL << i);

                    _eCoresMask = 0;
                    for (int i = 0; i < eCoreIdCount; i++) _eCoresMask |= (1UL << (eStart + i));

                    _topologyDetected = true;
                }
                else
                {
                    _pCoresMask = _allCoresMask;
                    _eCoresMask = _allCoresMask;
                    _topologyDetected = false;
                }
            }
        }

        public static void Start()
        {
            lock (_stateLock)
            {
                if (_isRunning) return;
                _isRunning = true;
                _monitorTimer?.Dispose();
                _monitorTimer = new System.Threading.Timer(OnMonitorTick, null, 1000, 2000);
                Logger.WriteLine("CpuAffinityManager started.");
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
            RestoreAllApplied();
        }

        /// <summary>Compute the absolute affinity mask for a rule.</summary>
        public static ulong ResolveMask(AffinityRule rule)
        {
            ulong baseMask;
            switch (rule.AffinityMode)
            {
                case MODE_PCORES:
                    ulong pm;
                    lock (_stateLock) pm = _pCoresMask;
                    baseMask = pm;
                    break;
                case MODE_ECORES:
                    ulong em;
                    lock (_stateLock) em = _eCoresMask;
                    baseMask = em;
                    break;
                case MODE_CUSTOM:
                    if (TryParseMask(rule.CustomMask, out ulong custom)) return custom;
                    lock (_stateLock) return _allCoresMask;
                case MODE_ALL:
                default:
                    lock (_stateLock) return _allCoresMask;
            }
            return baseMask;
        }

        public static bool TryParseMask(string text, out ulong mask)
        {
            mask = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
            try
            {
                mask = Convert.ToUInt64(text.Replace(",", ""), 16);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Apply affinity to a running process by pid. Returns true if applied.</summary>
        public static bool ApplyAffinityToPid(int pid, ulong mask)
        {
            if (pid <= 0 || mask == 0) return false;

            IntPtr hProcess = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_INFORMATION, false, (uint)pid);
            if (hProcess == IntPtr.Zero) return false;

            try
            {
                bool ok = SetProcessAffinityMask(hProcess, (IntPtr)mask);
                if (ok)
                {
                    lock (_stateLock)
                    {
                        _appliedPids[pid] = (int)mask;
                    }
                }
                return ok;
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }

        /// <summary>Apply affinity to every running instance of the given process name.</summary>
        public static int ApplyAffinityToProcess(string processName, ulong mask)
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
                        if (ApplyAffinityToPid(p.Id, mask)) applied++;
                    }
                    catch { }
                }
            }
            finally
            {
                foreach (var p in procs) p.Dispose();
            }

            if (applied > 0)
                Logger.WriteLine($"CpuAffinity: applied mask 0x{mask:X} to '{processName}' ({applied} instances)");
            return applied;
        }

        /// <summary>Manually apply a named preset to a running process (all/P/E/custom).</summary>
        public static int ApplyToProcessManual(string processName, int mode, ulong customMask = 0)
        {
            var rule = new AffinityRule { ProcessName = processName, AffinityMode = mode, CustomMask = customMask == 0 ? string.Empty : customMask.ToString("X") };
            return ApplyAffinityToProcess(processName, ResolveMask(rule));
        }

        private static void RestoreAllApplied()
        {
            Dictionary<int, int> copy;
            lock (_stateLock)
            {
                copy = new Dictionary<int, int>(_appliedPids);
                _appliedPids.Clear();
            }

            foreach (var kvp in copy)
            {
                try
                {
                    ulong allMask;
                    lock (_stateLock) allMask = _allCoresMask;
                    ApplyAffinityToPid(kvp.Key, allMask);
                }
                catch { }
            }
            Logger.WriteLine("CpuAffinityManager restored affinity on stop.");
        }

        private static void ApplyAllRules()
        {
            List<AffinityRule> rules;
            lock (_ruleLock) rules = new List<AffinityRule>(_rules);

            foreach (var rule in rules)
            {
                if (string.IsNullOrWhiteSpace(rule.ProcessName)) continue;
                ulong mask = ResolveMask(rule);
                ApplyAffinityToProcess(rule.ProcessName, mask);
            }
        }

        // Called every 2s: re-apply affinity while matching processes live, and
        // restore any previously applied pid that no longer matches a rule.
        private static void OnMonitorTick(object? state)
        {
            if (!IsEnabled) return;

            List<AffinityRule> rules;
            lock (_ruleLock) rules = new List<AffinityRule>(_rules);

            foreach (var rule in rules)
            {
                if (string.IsNullOrWhiteSpace(rule.ProcessName)) continue;
                ulong mask = ResolveMask(rule);

                var procs = System.Diagnostics.Process.GetProcessesByName(rule.ProcessName);
                try
                {
                    foreach (var p in procs)
                    {
                        try
                        {
                            ApplyAffinityToPid(p.Id, mask);
                        }
                        catch { }
                    }
                }
                finally
                {
                    foreach (var p in procs) p.Dispose();
                }
            }

            // Restore any tracked pid no longer matching any rule.
            HashSet<int> matching = new HashSet<int>();
            foreach (var rule in rules)
            {
                var procs = System.Diagnostics.Process.GetProcessesByName(rule.ProcessName);
                try
                {
                    foreach (var p in procs) matching.Add(p.Id);
                }
                finally
                {
                    foreach (var p in procs) p.Dispose();
                }
            }

            List<int> restorePids = new();
            lock (_stateLock)
            {
                foreach (var pid in _appliedPids.Keys)
                {
                    if (!matching.Contains(pid)) restorePids.Add(pid);
                }
            }

            foreach (var pid in restorePids)
            {
                try
                {
                    ulong allMask;
                    lock (_stateLock) allMask = _allCoresMask;
                    ApplyAffinityToPid(pid, allMask);
                    lock (_stateLock) _appliedPids.Remove(pid);
                }
                catch { }
            }
        }
    }
}
