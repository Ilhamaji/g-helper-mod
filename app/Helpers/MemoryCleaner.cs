using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace GHelper.Helpers
{
    public static class MemoryCleaner
    {
        // ── Native Win32 Structures & Imports ────────────────────────────────
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privilege;
        }

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
        private const string SE_INCREASE_QUOTA_NAME = "SeIncreaseQuotaPrivilege";
        private const string SE_PROFILE_SINGLE_PROCESS_NAME = "SeProfileSingleProcessPrivilege";

        // SystemMemoryListInformation Command Codes for NtSetSystemInformation
        private const int SYSTEM_MEMORY_LIST_INFORMATION = 80;
        private const int SYSTEM_MEMORY_LIST_COMMAND_EMPTY_WORKING_SETS = 2;
        private const int SYSTEM_MEMORY_LIST_COMMAND_PURGE_STANDBY_LIST = 4;
        private const int SYSTEM_MEMORY_LIST_COMMAND_PURGE_LOW_PRIORITY_STANDBY_LIST = 5;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] ref MEMORYSTATUSEX lpBuffer);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetSystemInformation(int SystemInformationClass, ref int SystemInformation, int SystemInformationLength);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("psapi.dll")]
        private static extern int EmptyWorkingSet(IntPtr hProcess);

        private static System.Threading.Timer? _autoCleanerTimer;
        private static bool _privilegesEnabled = false;

        // ── Public API ────────────────────────────────────────────────────────

        public static bool GetMemoryStatus(out ulong totalRamBytes, out ulong availRamBytes, out uint loadPercent)
        {
            totalRamBytes = 0;
            availRamBytes = 0;
            loadPercent = 0;

            MEMORYSTATUSEX stat = new MEMORYSTATUSEX();
            stat.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));

            if (GlobalMemoryStatusEx(ref stat))
            {
                totalRamBytes = stat.ullTotalPhys;
                availRamBytes = stat.ullAvailPhys;
                loadPercent = stat.dwMemoryLoad;
                return true;
            }

            return false;
        }

        public static long CleanMemory(bool purgeStandby = true, bool emptyWorkingSets = true)
        {
            GetMemoryStatus(out _, out ulong availBefore, out _);

            EnableMemoryPrivileges();

            if (emptyWorkingSets)
            {
                TrimProcessWorkingSets();
            }

            if (purgeStandby)
            {
                PurgeSystemStandbyList();
            }

            GetMemoryStatus(out _, out ulong availAfter, out _);

            long freedBytes = (long)availAfter - (long)availBefore;
            return freedBytes > 0 ? freedBytes : 0;
        }

        public static void SetAutoCleaner(bool enabled, int intervalMinutes = 15)
        {
            _autoCleanerTimer?.Dispose();
            _autoCleanerTimer = null;

            if (enabled)
            {
                int dueMs = 60 * 1000; // First check in 1 minute
                int periodMs = Math.Max(1, intervalMinutes) * 60 * 1000;
                _autoCleanerTimer = new System.Threading.Timer(OnAutoCleanerCallback, null, dueMs, periodMs);
            }
        }

        private static void OnAutoCleanerCallback(object? state)
        {
            try
            {
                if (GetMemoryStatus(out _, out ulong availBytes, out uint loadPercent))
                {
                    // Clean if RAM load exceeds 75% or available RAM drops below 2.5 GB
                    if (loadPercent >= 75 || availBytes < (2560UL * 1024 * 1024))
                    {
                        long freed = CleanMemory(purgeStandby: true, emptyWorkingSets: true);
                        Logger.WriteLine($"Auto RAM Cleaner executed: freed {freed / (1024 * 1024)} MB");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Auto RAM Cleaner error: " + ex.Message);
            }
        }

        // ── Helper Methods ────────────────────────────────────────────────────

        private static bool EnableMemoryPrivileges()
        {
            if (_privilegesEnabled) return true;

            IntPtr tokenHandle = IntPtr.Zero;
            try
            {
                IntPtr currentProcess = Process.GetCurrentProcess().Handle;
                if (!OpenProcessToken(currentProcess, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out tokenHandle))
                {
                    return false;
                }

                SetPrivilege(tokenHandle, SE_INCREASE_QUOTA_NAME, true);
                SetPrivilege(tokenHandle, SE_PROFILE_SINGLE_PROCESS_NAME, true);
                _privilegesEnabled = true;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (tokenHandle != IntPtr.Zero) CloseHandle(tokenHandle);
            }
        }

        private static bool SetPrivilege(IntPtr tokenHandle, string privilegeName, bool enable)
        {
            LUID luid;
            if (!LookupPrivilegeValue(null, privilegeName, out luid)) return false;

            TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privilege = new LUID_AND_ATTRIBUTES
                {
                    Luid = luid,
                    Attributes = enable ? SE_PRIVILEGE_ENABLED : 0
                }
            };

            return AdjustTokenPrivileges(tokenHandle, false, ref tp, (uint)Marshal.SizeOf(typeof(TOKEN_PRIVILEGES)), IntPtr.Zero, IntPtr.Zero);
        }

        private static bool PurgeSystemStandbyList()
        {
            try
            {
                int command = SYSTEM_MEMORY_LIST_COMMAND_PURGE_STANDBY_LIST;
                int result = NtSetSystemInformation(SYSTEM_MEMORY_LIST_INFORMATION, ref command, sizeof(int));

                int commandLow = SYSTEM_MEMORY_LIST_COMMAND_PURGE_LOW_PRIORITY_STANDBY_LIST;
                NtSetSystemInformation(SYSTEM_MEMORY_LIST_INFORMATION, ref commandLow, sizeof(int));

                return result == 0;
            }
            catch
            {
                return false;
            }
        }

        private static void TrimProcessWorkingSets()
        {
            try
            {
                int command = SYSTEM_MEMORY_LIST_COMMAND_EMPTY_WORKING_SETS;
                NtSetSystemInformation(SYSTEM_MEMORY_LIST_INFORMATION, ref command, sizeof(int));

                IntPtr currentProc = Process.GetCurrentProcess().Handle;
                EmptyWorkingSet(currentProc);
            }
            catch { }
        }
    }
}
