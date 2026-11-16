using Microsoft.Win32;
using System.Text;

namespace GHelper
{
    // Drives Equalizer APO's mic chain: RNNoise VST first, then a condenser EQ preset.
    // G-Helper owns ghelper_mic.txt; config.txt only gets one Include line.
    public static class EqualizerApo
    {
        const string CaptureApoClsid = "EACD2258-FCAC-4FF4-B36D-419E924A6D79";
        const string IncludeFile = "ghelper_mic.txt";
        const string DefaultDll = "rnnoise_mono.dll";
        const string RegKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\EqualizerAPO";

        // shortcut: starting points for a generic condenser, not tuned to a specific mic. Edit here to taste.
        static readonly Dictionary<string, string> PresetBody = new()
        {
            ["Flat"] = "",
            ["Condenser Clean"] =
                "Preamp: -2 dB\n" +
                "Filter: ON HPQ Fc 80 Hz Q 0.707\n" +
                "Filter: ON PK Fc 200 Hz Gain -2 dB Q 1.0\n" +
                "Filter: ON PK Fc 3500 Hz Gain 2 dB Q 1.0\n" +
                "Filter: ON PK Fc 7000 Hz Gain -3 dB Q 3.0\n",
            ["Condenser Warm"] =
                "Preamp: -3 dB\n" +
                "Filter: ON HPQ Fc 70 Hz Q 0.707\n" +
                "Filter: ON LSC Fc 120 Hz Gain 2 dB\n" +
                "Filter: ON PK Fc 300 Hz Gain -2.5 dB Q 1.2\n" +
                "Filter: ON PK Fc 4000 Hz Gain 1.5 dB Q 1.0\n" +
                "Filter: ON HSC Fc 10000 Hz Gain 1 dB\n",
            ["Condenser Broadcast"] =
                "Preamp: -4 dB\n" +
                "Filter: ON HPQ Fc 90 Hz Q 0.707\n" +
                "Filter: ON PK Fc 250 Hz Gain -3 dB Q 1.0\n" +
                "Filter: ON PK Fc 5000 Hz Gain 3 dB Q 1.0\n" +
                "Filter: ON PK Fc 6500 Hz Gain -2 dB Q 4.0\n" +
                "Filter: ON HSC Fc 9000 Hz Gain 2 dB\n",
        };

        public static string[] Presets => PresetBody.Keys.ToArray();

        public static string Preset
        {
            get => AppConfig.GetString("eq_preset", "Flat");
            set => AppConfig.Set("eq_preset", value);
        }

        // 0 = RNNoise off. Otherwise maps to the plugin's VAD threshold: higher gates non-speech harder.
        public static int Strength => Math.Clamp(AppConfig.Get("eq_rnnoise", 60), 0, 100);

        // Master switch: when off the generated chain is empty, so the mic runs with zero processing.
        // The VST costs ~20% of one core on low-end CPUs, and only while an app holds the microphone.
        public static bool Enabled
        {
            get => AppConfig.Get("eq_enabled", 1) == 1;
            set => AppConfig.Set("eq_enabled", value ? 1 : 0);
        }

        public static bool Installed => ConfigDir is not null;

        static string? ConfigDir => Registry.GetValue(RegKey, "ConfigPath", null) as string;
        static string? InstallDir => Registry.GetValue(RegKey, "InstallPath", null) as string;

        public static string DllPath()
        {
            string dll = AppConfig.GetString("eq_rnnoise_dll", DefaultDll);
            return Path.IsPathRooted(dll) ? dll : Path.Combine(InstallDir ?? "", "VSTPlugins", dll);
        }

        // Name of the first enabled capture endpoint that has Equalizer APO attached, or null.
        static string? ActiveMic()
        {
            using var capture = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture");
            if (capture is null) return null;

            foreach (string id in capture.GetSubKeyNames())
            {
                using var dev = capture.OpenSubKey(id);
                if (dev?.GetValue("DeviceState") is not int state || state != 1) continue;

                using var fx = dev.OpenSubKey("FxProperties");
                if (fx is null || !fx.GetValueNames().Any(n => (fx.GetValue(n) as string)?.Contains(CaptureApoClsid, StringComparison.OrdinalIgnoreCase) == true)) continue;

                using var props = dev.OpenSubKey("Properties");
                return props?.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},2") as string ?? id;
            }
            return null;
        }

        public static (bool ok, string text) Status()
        {
            string? dir = ConfigDir;
            if (dir is null) return (false, "✖ Equalizer APO not installed");

            if (!Enabled) return (true, "✔ Mic effects off — no processing, no CPU cost");

            string? mic = ActiveMic();
            if (mic is null) return (false, "✖ Equalizer APO is not attached to any active microphone. Run DeviceSelector.exe from its install folder.");

            string main = Path.Combine(dir, "config.txt");
            if (!File.Exists(Path.Combine(dir, IncludeFile)) || !File.ReadAllText(main).Contains(IncludeFile, StringComparison.OrdinalIgnoreCase))
                return (false, $"✖ Mic \"{mic}\" ready, nothing applied yet. Pick a preset.");

            if (Strength > 0 && !File.Exists(DllPath()))
                return (false, $"✖ RNNoise DLL not found: {DllPath()}");

            return (true, $"✔ Active on mic \"{mic}\" | Preset: {Preset} | RNNoise: {(Strength > 0 ? Strength + "%" : "off")}");
        }

        public static void Apply()
        {
            string dir = ConfigDir ?? throw new InvalidOperationException("Equalizer APO not installed");
            string main = Path.Combine(dir, "config.txt");

            var lines = File.Exists(main) ? File.ReadAllLines(main).ToList() : new List<string>();

            // Drop any hand-written RNNoise line, ours replaces it; running it twice would double-process.
            var kept = lines.Where(l => !(l.TrimStart().StartsWith("VSTPlugin:", StringComparison.OrdinalIgnoreCase)
                                          && l.Contains("rnnoise", StringComparison.OrdinalIgnoreCase))).ToList();
            if (!kept.Any(l => l.Trim().Equals("Include: " + IncludeFile, StringComparison.OrdinalIgnoreCase)))
                kept.Add("Include: " + IncludeFile);

            if (!kept.SequenceEqual(lines))
            {
                if (File.Exists(main) && !File.Exists(main + ".ghelper.bak")) File.Copy(main, main + ".ghelper.bak");
                File.WriteAllLines(main, kept);
            }

            File.WriteAllText(Path.Combine(dir, IncludeFile), BuildInclude());
        }

        static string BuildInclude()
        {
            var sb = new StringBuilder("# Generated by G-Helper. Manual edits are overwritten.\n");

            if (!Enabled) return sb.ToString();

            if (Strength > 0)
            {
                string dll = AppConfig.GetString("eq_rnnoise_dll", DefaultDll);
                if (dll.Contains(' ')) dll = $"\"{dll}\"";
                sb.Append($"VSTPlugin: Library {dll} ChunkData \"{RnnoiseChunk(Strength / 100.0)}\"\n");
            }

            if (PresetBody.TryGetValue(Preset, out var body)) sb.Append(body);
            return sb.ToString();
        }

        // VST2 chunk the rnnoise plugin saves: "VC2!" + int32 xml length + xml + NUL.
        static string RnnoiseChunk(double vadThreshold)
        {
            string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?> <RNNoise>" +
                "<PARAM id=\"vad_grace_period\" value=\"5.0\"/>" +
                "<PARAM id=\"vad_retroactive_grace_period\" value=\"0.0\"/>" +
                $"<PARAM id=\"vad_threshold\" value=\"{vadThreshold.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}\"/></RNNoise>";
            byte[] body = Encoding.UTF8.GetBytes(xml);

            var chunk = new List<byte>(Encoding.ASCII.GetBytes("VC2!"));
            chunk.AddRange(BitConverter.GetBytes(body.Length));
            chunk.AddRange(body);
            chunk.Add(0);
            return Convert.ToBase64String(chunk.ToArray());
        }
    }
}
