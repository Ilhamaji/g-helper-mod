using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GHelper.Helpers
{
    public class MicDeviceInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;

        public override string ToString() => Name;
    }

    public static class MicNoiseManager
    {
        private static readonly string ApoConfigDir = @"C:\Program Files\EqualizerAPO\config";
        private static readonly string MainConfigFile = Path.Combine(ApoConfigDir, "config.txt");
        private static readonly string MicConfigFile = Path.Combine(ApoConfigDir, "cpu_boost_mic.txt");

        private static string _lastWrittenContent = string.Empty;
        private static bool _includeDirectiveEnsured = false;
        private static bool _vstPluginsEnsured = false;

        public static bool IsApoInstalled()
        {
            try { return Directory.Exists(ApoConfigDir); }
            catch { return false; }
        }

        public static void EnsureIncludeDirective()
        {
            if (!IsApoInstalled()) return;
            if (_includeDirectiveEnsured) return;

            try
            {
                if (!File.Exists(MainConfigFile))
                {
                    File.WriteAllText(MainConfigFile, "# Equalizer APO Configuration\r\n");
                }

                string content = File.ReadAllText(MainConfigFile);
                if (content.IndexOf("cpu_boost_mic.txt", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    StringBuilder sb = new StringBuilder(content);
                    if (sb.Length > 0 && sb[sb.Length - 1] != '\n')
                    {
                        sb.AppendLine();
                    }
                    sb.AppendLine();
                    sb.AppendLine("# G-Helper - Mic Noise Suppression Integration");
                    sb.AppendLine("Include: cpu_boost_mic.txt");
                    File.WriteAllText(MainConfigFile, sb.ToString());
                }
                _includeDirectiveEnsured = true;
            }
            catch (Exception ex)
            {
                Logger.WriteLine("EnsureIncludeDirective error: " + ex.Message);
            }
        }

        private static string GetLocalPluginPath(string filename)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string path1 = Path.Combine(baseDir, "win-rnnoise", "vst", filename);
            if (File.Exists(path1)) return path1;

            string path2 = Path.Combine(baseDir, @"..\..\win-rnnoise", "vst", filename);
            if (File.Exists(path2)) return path2;

            string path3 = Path.Combine(baseDir, @"..\win-rnnoise", "vst", filename);
            if (File.Exists(path3)) return path3;

            return path1;
        }

        public static void EnsureVstPlugins()
        {
            if (_vstPluginsEnsured) return;
            try
            {
                string vstTargetDir = @"C:\Program Files\EqualizerAPO\VSTPlugins\vst";
                if (!Directory.Exists(vstTargetDir))
                {
                    Directory.CreateDirectory(vstTargetDir);
                }

                string rnnoiseTarget = Path.Combine(vstTargetDir, "rnnoise_mono.dll");
                if (!File.Exists(rnnoiseTarget))
                {
                    string localRnnoise = GetLocalPluginPath("rnnoise_mono.dll");
                    if (File.Exists(localRnnoise))
                    {
                        File.Copy(localRnnoise, rnnoiseTarget, true);
                    }
                }

                string ggateTarget = Path.Combine(vstTargetDir, "GGate.dll");
                if (!File.Exists(ggateTarget))
                {
                    string localGGate = GetLocalPluginPath("GGate.dll");
                    if (File.Exists(localGGate))
                    {
                        File.Copy(localGGate, ggateTarget, true);
                    }
                }
                _vstPluginsEnsured = true;
            }
            catch (Exception ex)
            {
                Logger.WriteLine("EnsureVstPlugins error: " + ex.Message);
            }
        }

        public static string ApplyMicConfig()
        {
            if (!IsApoInstalled())
            {
                return "Equalizer APO was not detected on this system.";
            }

            try
            {
                EnsureIncludeDirective();
                EnsureVstPlugins();

                bool isEnabled = AppConfig.Is("mic_noise_enabled");
                if (!isEnabled)
                {
                    WriteIfChanged(MicConfigFile, "# Mic Noise Reduction Feature Disabled\r\n");
                    return "Feature disabled. Microphone back to normal.";
                }

                string rnnoisePath = @"C:\Program Files\EqualizerAPO\VSTPlugins\vst\rnnoise_mono.dll";
                string ggatePath = @"C:\Program Files\EqualizerAPO\VSTPlugins\vst\GGate.dll";

                bool isRnnoiseEnabled = AppConfig.Is("mic_rnnoise_enabled");
                int presetProfile = AppConfig.Get("mic_preset_profile");
                double gateThreshold = AppConfig.Get("mic_gate_threshold"); // -100 to 0 dB
                double preampGain = AppConfig.Get("mic_preamp_gain"); // -20 to +20 dB
                string targetDevice = AppConfig.GetString("mic_target_device") ?? "all";

                StringBuilder sb = new StringBuilder(2048);
                sb.AppendLine("# G-Helper — Microphone Noise Suppression");
                sb.AppendLine("# Auto-generated. Do not edit manually.");
                sb.AppendLine();

                sb.AppendLine("Stage: capture");
                string deviceStr = string.IsNullOrEmpty(targetDevice) || targetDevice.Equals("all", StringComparison.OrdinalIgnoreCase) ? "all" : targetDevice.Trim();
                sb.AppendLine("Device: " + deviceStr);
                sb.AppendLine();

                if (presetProfile == 9)
                {
                    sb.AppendLine("# [Preset: Default — Raw Mic Input]");
                    sb.AppendLine("# Raw microphone input bypass: all filters disabled.");
                }
                else
                {
                    double hpfFc = 60;
                    if (presetProfile == 4) hpfFc = 65;
                    else if (presetProfile == 7) hpfFc = 80;
                    else if (presetProfile == 1) hpfFc = 50;
                    else if (presetProfile == 2) hpfFc = 40;

                    sb.AppendLine("# [Stage 2: Anti-rumble Subsonic HPF]");
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Filter: ON HP Fc {0:F0} Hz", hpfFc));
                    sb.AppendLine();

                    sb.AppendLine("# [Stage 3: RNNoise AI Noise Suppression]");
                    if (isRnnoiseEnabled)
                    {
                        string chunkData = GetRnnoiseChunkData(0.8, 100.0, 1.0);
                        sb.AppendLine("VSTPlugin: Library \"" + rnnoisePath + "\" ChunkData \"" + chunkData + "\"");
                    }
                    else
                    {
                        sb.AppendLine("# Bypassed by user setting");
                    }
                    sb.AppendLine();

                    double gateParamValue = (100.0 + gateThreshold) / 100.0;
                    gateParamValue = Math.Max(0.0, Math.Min(1.0, gateParamValue));
                    string ggateParam = string.Format(CultureInfo.InvariantCulture, " Thresh {0:F4}", gateParamValue);

                    sb.AppendLine("# [Stage 3.5: Volume-based Noise Gate]");
                    if (File.Exists(ggatePath))
                    {
                        sb.AppendLine("VSTPlugin: Library \"" + ggatePath + "\"" + ggateParam);
                    }
                    else
                    {
                        sb.AppendLine("# Warning: GGate.dll not found, volume-based gate skipped");
                    }
                    sb.AppendLine();

                    if (Math.Abs(preampGain) > 0.01)
                    {
                        sb.AppendLine("# [Stage 4: Post-AI Gain Recovery + Preamp]");
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Preamp: {0:F1} dB", preampGain));
                        sb.AppendLine();
                    }

                    AppendPresetEQ(sb, presetProfile);
                }

                sb.AppendLine();
                sb.AppendLine("# [Reset state back to defaults]");
                sb.AppendLine("Stage: playback");
                sb.AppendLine("Device: all");

                bool written = WriteIfChanged(MicConfigFile, sb.ToString());
                return written ? "Microphone configuration updated!" : "Configuration up to date.";
            }
            catch (Exception ex)
            {
                return "Config Write Error: " + ex.Message;
            }
        }

        private static void AppendPresetEQ(StringBuilder sb, int presetProfile)
        {
            switch (presetProfile)
            {
                case 0:
                    sb.AppendLine("# [Preset: Studio Podcast Pro - Mastered Studio Broadcast]");
                    sb.AppendLine("Filter: ON PK Fc 130 Hz Gain 3.5 dB Q 1.00");
                    sb.AppendLine("Filter: ON PK Fc 300 Hz Gain -4.0 dB Q 1.20");
                    sb.AppendLine("Filter: ON PK Fc 3200 Hz Gain 3.5 dB Q 1.20");
                    sb.AppendLine("Filter: ON PK Fc 6200 Hz Gain -3.0 dB Q 3.00");
                    sb.AppendLine("Filter: ON HS Fc 9500 Hz Gain 3.5 dB");
                    sb.AppendLine("Filter: ON LP Fc 16000 Hz");
                    break;
                case 1:
                    sb.AppendLine("# [Preset: Karaoke Vocal Master - Studio Singing Tone]");
                    sb.AppendLine("Preamp: 2.0 dB");
                    sb.AppendLine("Filter: ON PK Fc 160 Hz Gain 5.5 dB Q 0.95");
                    sb.AppendLine("Filter: ON PK Fc 480 Hz Gain -4.5 dB Q 1.10");
                    sb.AppendLine("Filter: ON PK Fc 2800 Hz Gain 6.5 dB Q 1.20");
                    sb.AppendLine("Filter: ON PK Fc 5500 Hz Gain 4.5 dB Q 1.40");
                    sb.AppendLine("Filter: ON HS Fc 10000 Hz Gain 6.0 dB");
                    break;
                case 2:
                    sb.AppendLine("# [Preset: Podcast Warm & Deep - Radio Host Voice]");
                    sb.AppendLine("Filter: ON PK Fc 95 Hz Gain 6.0 dB Q 0.85");
                    sb.AppendLine("Filter: ON PK Fc 280 Hz Gain -4.5 dB Q 1.00");
                    sb.AppendLine("Filter: ON PK Fc 3200 Hz Gain 4.0 dB Q 1.20");
                    sb.AppendLine("Filter: ON HS Fc 8500 Hz Gain 4.0 dB");
                    break;
                case 3:
                    sb.AppendLine("# [Preset: Studio Condenser Crisp - Modern Airy Voice]");
                    sb.AppendLine("Filter: ON PK Fc 180 Hz Gain -2.5 dB Q 1.00");
                    sb.AppendLine("Filter: ON PK Fc 3800 Hz Gain 5.0 dB Q 1.20");
                    sb.AppendLine("Filter: ON PK Fc 6500 Hz Gain -2.5 dB Q 3.00");
                    sb.AppendLine("Filter: ON HS Fc 9500 Hz Gain 5.5 dB");
                    break;
                case 4:
                    sb.AppendLine("# [Preset: Gamer Streamer Pro - Focus & Keyclick Control]");
                    sb.AppendLine("Filter: ON PK Fc 250 Hz Gain -3.0 dB Q 1.00");
                    sb.AppendLine("Filter: ON PK Fc 2500 Hz Gain 4.5 dB Q 1.20");
                    sb.AppendLine("Filter: ON PK Fc 4500 Hz Gain 3.5 dB Q 1.50");
                    sb.AppendLine("Filter: ON LP Fc 12000 Hz");
                    break;
                case 5:
                    sb.AppendLine("# [Preset: Acoustic Vocal & Music - Live Acoustic]");
                    sb.AppendLine("Filter: ON PK Fc 150 Hz Gain 3.5 dB Q 1.00");
                    sb.AppendLine("Filter: ON PK Fc 2200 Hz Gain 3.5 dB Q 1.20");
                    sb.AppendLine("Filter: ON HS Fc 8000 Hz Gain 4.0 dB");
                    break;
                case 6:
                    sb.AppendLine("# [Preset: Pure AI Studio Suppressor - Flat Response]");
                    break;
                case 7:
                    sb.AppendLine("# [Preset: Extreme Noise Isolation - Loud Environment]");
                    sb.AppendLine("Filter: ON PK Fc 300 Hz Gain -5.0 dB Q 0.80");
                    sb.AppendLine("Filter: ON PK Fc 2500 Hz Gain 4.0 dB Q 1.50");
                    sb.AppendLine("Filter: ON LP Fc 9000 Hz");
                    break;
                default:
                    sb.AppendLine("# [Preset: Default — Studio Podcast]");
                    break;
            }
        }

        private static string GetRnnoiseChunkData(double threshold, double gracePeriod, double retroactiveGracePeriod)
        {
            string xml = string.Format(
                CultureInfo.InvariantCulture,
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?> <RNNoise><PARAM id=\"vad_grace_period\" value=\"{0:F1}\"/><PARAM id=\"vad_retroactive_grace_period\" value=\"{1:F1}\"/><PARAM id=\"vad_threshold\" value=\"{2:0.0#######}\"/></RNNoise>",
                gracePeriod, retroactiveGracePeriod, threshold
            );
            byte[] xmlBytes = Encoding.UTF8.GetBytes(xml);
            byte[] allBytes = new byte[8 + xmlBytes.Length + 1];

            allBytes[0] = 0x56; allBytes[1] = 0x43; allBytes[2] = 0x32; allBytes[3] = 0x21;

            int len = xmlBytes.Length + 1;
            allBytes[4] = (byte)(len & 0xFF);
            allBytes[5] = (byte)((len >> 8) & 0xFF);
            allBytes[6] = (byte)((len >> 16) & 0xFF);
            allBytes[7] = (byte)((len >> 24) & 0xFF);

            Array.Copy(xmlBytes, 0, allBytes, 8, xmlBytes.Length);
            return Convert.ToBase64String(allBytes);
        }

        private static bool WriteIfChanged(string path, string content)
        {
            if (content == _lastWrittenContent && File.Exists(path))
            {
                return false;
            }

            File.WriteAllText(path, content);
            _lastWrittenContent = content;
            return true;
        }

        // ── CoreAudio P/Invoke for Capture Device Enumeration ─────────────────
        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumAudioEndpointsDelegate(IntPtr pThis, int dataFlow, int dwStateMask, out IntPtr ppDevices);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetCountDelegate(IntPtr pThis, out uint pcDevices);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ItemDelegate(IntPtr pThis, uint nDevice, out IntPtr ppDevice);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int OpenPropertyStoreDelegate(IntPtr pThis, uint stgmAccess, out IntPtr ppProperties);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetIdDelegate(IntPtr pThis, out IntPtr ppstrId);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetValueDelegate(IntPtr pThis, ref PropertyKey key, out PropVariant pv);

        [StructLayout(LayoutKind.Sequential)]
        internal struct PropertyKey
        {
            public Guid fmtid;
            public uint pid;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct PropVariant
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr pointerValue;
        }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant pvar);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

        private static T GetVTableDelegate<T>(IntPtr pUnk, int vtableIndex) where T : class
        {
            IntPtr vtable = Marshal.ReadIntPtr(pUnk);
            IntPtr methodPtr = Marshal.ReadIntPtr(vtable, vtableIndex * IntPtr.Size);
            return (Marshal.GetDelegateForFunctionPointer(methodPtr, typeof(T)) as T)!;
        }

        public static List<MicDeviceInfo> GetCaptureDevices()
        {
            var devices = new List<MicDeviceInfo>
            {
                new MicDeviceInfo { Name = "All Devices (Global)", Id = "all" }
            };

            IntPtr enumPtr = IntPtr.Zero;
            IntPtr collectionPtr = IntPtr.Zero;

            try
            {
                enumPtr = Marshal.GetIUnknownForObject(new MMDeviceEnumerator());
                var enumAudioEndpoints = GetVTableDelegate<EnumAudioEndpointsDelegate>(enumPtr, 3);

                if (enumAudioEndpoints(enumPtr, 1, 0xF, out collectionPtr) == 0 && collectionPtr != IntPtr.Zero) // 1 = eCapture
                {
                    var getCount = GetVTableDelegate<GetCountDelegate>(collectionPtr, 3);
                    var getItem = GetVTableDelegate<ItemDelegate>(collectionPtr, 4);

                    if (getCount(collectionPtr, out uint count) == 0)
                    {
                        for (uint i = 0; i < count; i++)
                        {
                            IntPtr devPtr = IntPtr.Zero;
                            if (getItem(collectionPtr, i, out devPtr) == 0 && devPtr != IntPtr.Zero)
                            {
                                try
                                {
                                    var openProps = GetVTableDelegate<OpenPropertyStoreDelegate>(devPtr, 4);
                                    var getId = GetVTableDelegate<GetIdDelegate>(devPtr, 5);

                                    getId(devPtr, out IntPtr idPtr);
                                    string id = Marshal.PtrToStringUni(idPtr) ?? string.Empty;
                                    if (idPtr != IntPtr.Zero) CoTaskMemFree(idPtr);

                                    if (openProps(devPtr, 0, out IntPtr propsPtr) == 0 && propsPtr != IntPtr.Zero)
                                    {
                                        try
                                        {
                                            string name = ReadPropertyString(propsPtr, new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14) // PKEY_Device_FriendlyName
                                                          ?? ("Microphone " + (i + 1));
                                            devices.Add(new MicDeviceInfo { Name = name, Id = id });
                                        }
                                        finally { Marshal.Release(propsPtr); }
                                    }
                                }
                                catch { }
                                finally { Marshal.Release(devPtr); }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("GetCaptureDevices error: " + ex.Message);
            }
            finally
            {
                if (collectionPtr != IntPtr.Zero) Marshal.Release(collectionPtr);
                if (enumPtr != IntPtr.Zero) Marshal.Release(enumPtr);
            }

            return devices;
        }

        private static string? ReadPropertyString(IntPtr propsPtr, Guid fmtid, uint pid)
        {
            var getValue = GetVTableDelegate<GetValueDelegate>(propsPtr, 5);
            var key = new PropertyKey { fmtid = fmtid, pid = pid };
            PropVariant pv = new PropVariant();
            try
            {
                if (getValue(propsPtr, ref key, out pv) == 0 && pv.pointerValue != IntPtr.Zero)
                {
                    return Marshal.PtrToStringUni(pv.pointerValue);
                }
            }
            catch { }
            finally { PropVariantClear(ref pv); }
            return null;
        }
    }
}
