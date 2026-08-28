using System;
using System.Threading;
using GHelper.Mode;

namespace GHelper.Helpers
{
    /// <summary>
    /// Idle Boost Guard — protects against CPU-boost-induced freezes at idle.
    /// When the CPU is idle for a sustained period (and AppAutoBoost is not
    /// managing a matched app), the active CPU boost mode is temporarily
    /// lowered to a safe mode and restored once activity resumes.
    /// </summary>
    public static class IdleBoostGuard
    {
        private static readonly object _lock = new();
        private static System.Threading.Timer? _guardTimer;
        private static bool _running;
        private static bool _guardActive;
        private static bool _safeHold;
        private static int _savedBoost = -1;
        private static int _idleStreak;

        public static bool IsEnabled
        {
            get => AppConfig.Is("idle_boost_guard");
            set
            {
                AppConfig.Set("idle_boost_guard", value ? 1 : 0);
                if (value) Start();
                else Stop();
            }
        }

        public static int IdleUsageThreshold =>
            Math.Clamp(AppConfig.Get("idle_boost_cpu_threshold", 10), 1, 50);

        public static int HoldSeconds =>
            Math.Clamp(AppConfig.Get("idle_boost_hold_seconds", 10), 3, 60);

        public static int SafeBoostMode
        {
            get
            {
                int mode = AppConfig.Get("idle_boost_safe_mode", 3);
                return mode is 0 or 1 or 3 or 4 or 5 or 6 ? mode : 3;
            }
        }

        public static bool IsGuardActive
        {
            get { lock (_lock) return _guardActive; }
        }

        public static void Initialize()
        {
            if (IsEnabled) Start();
        }

        public static void Start()
        {
            lock (_lock)
            {
                if (_running) return;
                _running = true;
                _guardActive = false;
                _safeHold = false;
                _savedBoost = -1;
                _idleStreak = 0;
                _guardTimer?.Dispose();
                _guardTimer = new System.Threading.Timer(OnGuardTick, null, 1000, 1000);
                Logger.WriteLine("IdleBoostGuard started.");
            }
        }

        public static void Stop()
        {
            lock (_lock)
            {
                if (!_running) return;
                _running = false;
                RestoreLocked();
                _guardTimer?.Dispose();
                _guardTimer = null;
                Logger.WriteLine("IdleBoostGuard stopped.");
            }
        }

        public static void Reset()
        {
            lock (_lock)
            {
                _idleStreak = 0;
                _safeHold = false;
                RestoreLocked();
            }
        }

        // Must be called while holding _lock.
        private static void RestoreLocked()
        {
            if (_guardActive && _savedBoost >= 0)
            {
                PowerNative.SetCPUBoost(_savedBoost);
                Logger.WriteLine($"IdleBoostGuard restored CPU Boost to mode {_savedBoost}");
            }
            _guardActive = false;
            _savedBoost = -1;
        }

        private static void OnGuardTick(object? state)
        {
            lock (_lock)
            {
                if (!_running) return;

                // Yield to AppAutoBoost: while it manages boost for a matched
                // app the guard must never interfere.
                if (AppAutoBoostManager.IsBoostManagedActive)
                {
                    RestoreLocked();
                    _safeHold = false;
                    _idleStreak = 0;
                    return;
                }

                int? usage = HardwareControl.GetCPUUsage();
                if (usage is null) return;

                if (usage.Value >= IdleUsageThreshold)
                {
                    RestoreLocked();
                    _safeHold = false;
                    _idleStreak = 0;
                    return;
                }

                _idleStreak++;

                if (_guardActive)
                {
                    int mode = PowerNative.GetCPUBoost();
                    if (mode >= 0 && mode != SafeBoostMode)
                        PowerNative.SetCPUBoost(SafeBoostMode);
                    return;
                }

                if (_safeHold) return;

                if (_idleStreak >= HoldSeconds)
                {
                    int current = PowerNative.GetCPUBoost();
                    if (current >= 0 && current != SafeBoostMode)
                    {
                        _savedBoost = current;
                        PowerNative.SetCPUBoost(SafeBoostMode);
                        _guardActive = true;
                        Logger.WriteLine($"IdleBoostGuard: CPU idle {_idleStreak}s, switched CPU Boost to safe mode {SafeBoostMode} (was {current})");
                    }
                    else
                    {
                        // Already safe or boost unreadable — nothing to lower.
                        _safeHold = true;
                    }
                }
            }
        }
    }
}