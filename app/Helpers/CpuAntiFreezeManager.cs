using System;
using System.Runtime.InteropServices;
using System.Threading;
using GHelper.Mode;

namespace GHelper.Helpers
{
    public static class CpuAntiFreezeManager
    {
        // ── P/Invoke for Power Management ────────────────────────────────────
        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuidPtr);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteACValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, uint AcValueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteDCValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, uint DcValueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadACValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, out uint AcValueIndex);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        private const uint ES_SYSTEM_REQUIRED = 0x00000001;
        private const uint ES_CONTINUOUS      = 0x80000000;

        private static readonly Guid GUID_PROCESSOR_SUBGROUP  = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        private static readonly Guid GUID_PROCTHROTTLEMIN     = new Guid("893dee8e-2bef-41e0-89c6-b55d0929964c");
        private static readonly Guid GUID_PCIEXPRESS_SUBGROUP = new Guid("503b4e44-eb92-42c7-9e7c-41149f485132");
        private static readonly Guid GUID_PCIEXPRESS_LINKSETTINGS = new Guid("ee12f90a-a8b9-470a-817c-ae015a4d22a3");

        private static Timer? _pulseTimer;
        private static bool _isAntiFreezeActive = false;
        private static uint _originalMinState = 5;
        private static uint _originalPcieState = 0;
        private static uint _lastAppliedMinState = 999;
        private static uint _lastAppliedPcieState = 999;

        public static bool IsEnabled
        {
            get => AppConfig.Is("cpu_anti_freeze_enabled");
            set
            {
                AppConfig.Set("cpu_anti_freeze_enabled", value ? 1 : 0);
                ApplyState();
            }
        }

        public static uint MinimumStateFloor
        {
            get
            {
                int val = AppConfig.Get("cpu_anti_freeze_min_floor");
                return val > 0 ? (uint)val : 15;
            }
            set
            {
                AppConfig.Set("cpu_anti_freeze_min_floor", (int)value);
                if (_isAntiFreezeActive) ApplyState();
            }
        }

        public static void Initialize()
        {
            _originalMinState = GetMinimumProcessorState();
            _originalPcieState = GetPcieLinkStatePowerManagement();
            ApplyState();
        }

        public static void ApplyState()
        {
            bool shouldBeActive = IsEnabled;

            if (shouldBeActive)
            {
                if (!_isAntiFreezeActive)
                {
                    _originalMinState = GetMinimumProcessorState();
                    _originalPcieState = GetPcieLinkStatePowerManagement();
                }

                uint floor = MinimumStateFloor;
                SetMinimumProcessorState(floor);
                SetPcieLinkStatePowerManagement(0); // 0 = Off (Max Performance, avoids PCIe link sleep freeze)
                StartKeepAlivePulse();
                _isAntiFreezeActive = true;
                Logger.WriteLine($"CpuAntiFreeze active (Min State Floor: {floor}%, Keep-Alive Pulse ON, PCIe ASPM Off)");
            }
            else
            {
                if (_isAntiFreezeActive)
                {
                    StopKeepAlivePulse();
                    SetMinimumProcessorState(_originalMinState);
                    SetPcieLinkStatePowerManagement(_originalPcieState);
                    _isAntiFreezeActive = false;
                    Logger.WriteLine("CpuAntiFreeze deactivated (Restored original settings)");
                }
            }
        }

        public static Guid GetActiveScheme()
        {
            IntPtr ptr = IntPtr.Zero;
            uint res = PowerGetActiveScheme(IntPtr.Zero, out ptr);
            if (res == 0 && ptr != IntPtr.Zero)
            {
                Guid g = (Guid)Marshal.PtrToStructure(ptr, typeof(Guid))!;
                LocalFree(ptr);
                return g;
            }
            return Guid.Empty;
        }

        public static void SetMinimumProcessorState(uint percentage)
        {
            if (percentage > 100) percentage = 100;
            if (_lastAppliedMinState == percentage) return;

            Guid activeScheme = GetActiveScheme();
            if (activeScheme != Guid.Empty)
            {
                Guid sub = GUID_PROCESSOR_SUBGROUP;
                Guid minState = GUID_PROCTHROTTLEMIN;
                PowerWriteACValueIndex(IntPtr.Zero, ref activeScheme, ref sub, ref minState, percentage);
                PowerWriteDCValueIndex(IntPtr.Zero, ref activeScheme, ref sub, ref minState, percentage);
                PowerSetActiveScheme(IntPtr.Zero, ref activeScheme);
                _lastAppliedMinState = percentage;
            }
        }

        public static uint GetMinimumProcessorState()
        {
            Guid activeScheme = GetActiveScheme();
            if (activeScheme != Guid.Empty)
            {
                Guid sub = GUID_PROCESSOR_SUBGROUP;
                Guid minGuid = GUID_PROCTHROTTLEMIN;
                uint res = PowerReadACValueIndex(IntPtr.Zero, ref activeScheme, ref sub, ref minGuid, out uint minState);
                if (res == 0) return minState;
            }
            return 5;
        }

        public static void SetPcieLinkStatePowerManagement(uint valueIndex)
        {
            if (valueIndex > 2) valueIndex = 0;
            if (_lastAppliedPcieState == valueIndex) return;

            Guid activeScheme = GetActiveScheme();
            if (activeScheme != Guid.Empty)
            {
                Guid sub = GUID_PCIEXPRESS_SUBGROUP;
                Guid link = GUID_PCIEXPRESS_LINKSETTINGS;
                PowerWriteACValueIndex(IntPtr.Zero, ref activeScheme, ref sub, ref link, valueIndex);
                PowerWriteDCValueIndex(IntPtr.Zero, ref activeScheme, ref sub, ref link, valueIndex);
                PowerSetActiveScheme(IntPtr.Zero, ref activeScheme);
                _lastAppliedPcieState = valueIndex;
            }
        }

        public static uint GetPcieLinkStatePowerManagement()
        {
            Guid activeScheme = GetActiveScheme();
            if (activeScheme != Guid.Empty)
            {
                Guid sub = GUID_PCIEXPRESS_SUBGROUP;
                Guid link = GUID_PCIEXPRESS_LINKSETTINGS;
                uint res = PowerReadACValueIndex(IntPtr.Zero, ref activeScheme, ref sub, ref link, out uint valueIndex);
                if (res == 0) return valueIndex;
            }
            return 0;
        }

        private static void StartKeepAlivePulse()
        {
            _pulseTimer?.Dispose();
            _pulseTimer = new Timer(OnKeepAlivePulseCallback, null, 0, 500);
        }

        private static void StopKeepAlivePulse()
        {
            _pulseTimer?.Dispose();
            _pulseTimer = null;
            try
            {
                SetThreadExecutionState(ES_CONTINUOUS);
            }
            catch { }
        }

        private static void OnKeepAlivePulseCallback(object? state)
        {
            try
            {
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
            }
            catch { }
        }
    }
}
