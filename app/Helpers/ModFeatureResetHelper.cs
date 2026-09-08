using System;
using System.Text;
using GHelper.Mode;

namespace GHelper.Helpers
{
    /// <summary>
    /// Safely and comprehensively restores all G-Helper Mod modifications to clean,
    /// stock system and Windows defaults. Clears any residual power throttling,
    /// priority overrides, custom affinity masks, and audio DSP filters.
    /// </summary>
    public static class ModFeatureResetHelper
    {
        public static string RestoreAllToSystemDefaults()
        {
            var report = new StringBuilder();
            report.AppendLine("=== G-Helper Mod Feature Reset Report ===");

            try
            {
                // 1. App Auto Boost & Discord Priority
                AppAutoBoostManager.StopService();
                AppAutoBoostManager.OptimizeDiscord(false);
                AppAutoBoostManager.ResetDefaultBoost();
                AppAutoBoostManager.IsEnabled = false;
                report.AppendLine("✓ App Auto Boost stopped & Discord process priority restored to Normal.");

                // 2. Restore active profile's CPU Boost
                int profileBoost = AppConfig.GetMode("auto_boost");
                if (profileBoost >= 0)
                {
                    PowerNative.SetCPUBoost(profileBoost);
                    report.AppendLine($"✓ CPU Boost restored to profile default (mode {profileBoost}).");
                }
                else
                {
                    PowerNative.SetCPUBoost(2); // Standard Aggressive
                    report.AppendLine("✓ CPU Boost restored to standard Aggressive (mode 2).");
                }

                // 3. CPU Anti-Freeze
                CpuAntiFreezeManager.IsEnabled = false;
                CpuAntiFreezeManager.ResetToCleanDefaults();
                report.AppendLine("✓ CPU Anti-Freeze disabled (Processor Min State reset to 5%, ASPM restored).");

                // 4. Auto Standby RAM Cleaner
                AppConfig.Set("auto_ram_cleaner_enabled", 0);
                MemoryCleaner.SetAutoCleaner(false);
                report.AppendLine("✓ Auto Standby RAM Cleaner disabled.");

                // 5. Microphone Noise EQ / VST DSP
                AppConfig.Set("mic_noise_enabled", 0);
                MicNoiseManager.ApplyMicConfig();
                report.AppendLine("✓ Microphone Noise EQ disabled & audio reset to raw bit-perfect stream.");

                Logger.WriteLine("ModFeatureResetHelper: Successfully restored all features to system defaults.");
            }
            catch (Exception ex)
            {
                report.AppendLine($"⚠ Error during reset: {ex.Message}");
                Logger.WriteLine("ModFeatureResetHelper error: " + ex.Message);
            }

            return report.ToString();
        }
    }
}
