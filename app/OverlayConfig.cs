using GHelper.Overlay;
using GHelper.UI;

namespace GHelper
{
    public partial class OverlayConfig : RForm
    {
        RButton[] modeButtons;
        (RCheckBox box, string key)[] blocks;
        int mode;
        bool updating;

        bool eqLoading;

        public OverlayConfig()
        {
            InitializeComponent();
            InitTheme(true);

            Text = Properties.Strings.Overlay;
            checkEnable.Text = Properties.Strings.Overlay;
            checkGameOnly.Text = Properties.Strings.OverlayOnlyInGames;
            buttonLight.Text = Properties.Strings.OverlayModeLight;
            buttonDefault.Text = Properties.Strings.Default;
            buttonFull.Text = Properties.Strings.OverlayModeFull;
            buttonComplete.Text = Properties.Strings.OverlayModeComplete;
            checkTemp.Text = Properties.Strings.OverlayTemperatures;
            checkFans.Text = Properties.Strings.FanSpeed;
            checkChart.Text = Properties.Strings.OverlayChart;
            checkPower.Text = Properties.Strings.Power;
            checkUsage.Text = Properties.Strings.OverlayLoad;
            checkBattery.Text = Properties.Strings.Battery;
            checkLabels.Text = Properties.Strings.OverlayLabels;
            checkBoost.Text = Properties.Strings.CPUBoost;
            labelSizeTitle.Text = Properties.Strings.OverlaySize;
            labelAlphaTitle.Text = Properties.Strings.OverlayTransparency;
            labelHotkeys.Text = "•  Ctrl + Shift + Alt + O - Toggle overlay\n•  Ctrl + Shift + Alt + Mouse Drag - Move overlay\n•  Ctrl + Shift + Alt + Mouse Click - Switch mode\n•  Ctrl + Shift + Alt + Wheel - Resize overlay\n•  Ctrl + Shift + Alt + Wheel Click - Reset size";
            buttonCpuColor.Text = "CPU " + Properties.Strings.Color;
            buttonGpuColor.Text = "GPU " + Properties.Strings.Color;
            buttonReset.Text = Properties.Strings.Reset;

            InitEqualizer();

            modeButtons = new[] { buttonDefault, buttonLight, buttonFull, buttonComplete };

            blocks = new[] {
                (checkFps, "overlay_show_fps"),
                (checkTemp, "overlay_show_temp"),
                (checkFans, "overlay_show_fans"),
                (checkChart, "overlay_show_chart"),
                (checkPower, "overlay_show_power"),
                (checkUsage, "overlay_show_usage"),
                (checkClocks, "overlay_show_clocks"),
                (checkRam, "overlay_show_ram"),
                (checkLabels, "overlay_names"),
                (checkInline, "overlay_inline"),
                (checkBoost, "overlay_show_boost"),
            };

            checkEnable.Checked = AppConfig.IsOverlay();
            checkGameOnly.Checked = AppConfig.IsOverlayGameOnly();

            mode = Math.Clamp(AppConfig.Get("overlay_mode", 0), 0, modeButtons.Length - 1);
            VisualiseBlocks();

            trackScale.Value = Math.Clamp(AppConfig.Get("overlay_scale_percent", 100), trackScale.Minimum, trackScale.Maximum);
            trackAlpha.Value = Math.Clamp(AppConfig.Get("overlay_alpha", 128), trackAlpha.Minimum, trackAlpha.Maximum);
            labelSize.Text = trackScale.Value + "%";
            labelAlpha.Text = AlphaText();

            buttonCpuColor.SwatchColor = HardwareOverlay.CpuColor;
            buttonGpuColor.SwatchColor = HardwareOverlay.GpuColor;

            checkEnable.CheckedChanged += (s, e) => { if (checkEnable.Checked != AppConfig.IsOverlay()) Program.settingsForm.ToggleOverlay(); };
            checkGameOnly.CheckedChanged += (s, e) => { if (checkGameOnly.Checked != AppConfig.IsOverlayGameOnly()) Program.settingsForm.ToggleOverlayGameOnly(); };

            buttonLight.BorderColor = colorEco;
            buttonDefault.BorderColor = colorStandard;
            buttonFull.BorderColor = colorTurbo;
            buttonComplete.BorderColor = colorCustom;

            foreach (var button in modeButtons) button.Click += ButtonMode_Click;
            foreach (var (box, _) in blocks) box.CheckedChanged += Block_CheckedChanged;
            checkBattery.CheckStateChanged += Battery_CheckStateChanged;

            trackScale.ValueChanged += TrackScale_ValueChanged;
            trackAlpha.ValueChanged += TrackAlpha_ValueChanged;

            buttonCpuColor.Click += (s, e) => PickColor("overlay_color_cpu", buttonCpuColor, HardwareOverlay.CpuColor);
            buttonGpuColor.Click += (s, e) => PickColor("overlay_color_gpu", buttonGpuColor, HardwareOverlay.GpuColor);
            buttonReset.Click += ButtonReset_Click;

            Shown += (s, e) => FormPosition();
        }

        public void FormPosition()
        {
            Top = Program.settingsForm.Top;
            Left = Program.settingsForm.Left - Width - 5;
        }

        private string AlphaText() => Math.Round(trackAlpha.Value * 100.0 / 255) + "%";

        private void ButtonReset_Click(object? sender, EventArgs e)
        {
            for (int m = 0; m < modeButtons.Length; m++)
            {
                foreach (var (_, key) in blocks) AppConfig.Remove(HardwareOverlay.ModeKey(key, m));
                AppConfig.Remove(HardwareOverlay.ModeKey("overlay_show_battery", m));
            }
            AppConfig.Remove("overlay_scale_percent");
            AppConfig.Remove("overlay_alpha");
            AppConfig.Remove("overlay_color_cpu");
            AppConfig.Remove("overlay_color_gpu");
            Program.hardwareOverlay?.RefreshSettings();

            VisualiseBlocks();
            trackScale.Value = 100;
            trackAlpha.Value = 128;
            buttonCpuColor.SwatchColor = HardwareOverlay.CpuColor;
            buttonGpuColor.SwatchColor = HardwareOverlay.GpuColor;
        }

        private void VisualiseBlocks()
        {
            updating = true;
            for (int i = 0; i < modeButtons.Length; i++) modeButtons[i].Activated = i == mode;
            foreach (var (box, key) in blocks) box.Checked = HardwareOverlay.BlockShown(key, mode);
            int battery = HardwareOverlay.BatteryState(mode);
            checkBattery.CheckState = battery < 0 ? CheckState.Indeterminate : (CheckState)battery;
            updating = false;
        }

        private void ButtonMode_Click(object? sender, EventArgs e)
        {
            mode = Array.IndexOf(modeButtons, sender);
            AppConfig.Set("overlay_mode", mode);
            VisualiseBlocks();
            Program.hardwareOverlay?.RefreshSettings();
        }

        private void Block_CheckedChanged(object? sender, EventArgs e)
        {
            if (updating) return;
            foreach (var (box, key) in blocks)
                if (box == sender) AppConfig.Set(HardwareOverlay.ModeKey(key, mode), box.Checked ? 1 : 0);
            Program.hardwareOverlay?.RefreshSettings();
        }

        private void Battery_CheckStateChanged(object? sender, EventArgs e)
        {
            if (updating) return;
            int state = checkBattery.CheckState == CheckState.Indeterminate ? -1 : (int)checkBattery.CheckState;
            AppConfig.Set(HardwareOverlay.ModeKey("overlay_show_battery", mode), state);
            Program.hardwareOverlay?.RefreshSettings();
        }

        private void TrackScale_ValueChanged(object? sender, EventArgs e)
        {
            labelSize.Text = trackScale.Value + "%";
            AppConfig.Set("overlay_scale_percent", trackScale.Value);
            Program.hardwareOverlay?.RefreshSettings();
        }

        private void TrackAlpha_ValueChanged(object? sender, EventArgs e)
        {
            labelAlpha.Text = AlphaText();
            AppConfig.Set("overlay_alpha", trackAlpha.Value);
            Program.hardwareOverlay?.RefreshSettings();
        }

        private void PickColor(string key, RColorButton button, Color initial)
        {
            RColorPicker colorDlg = new RColorPicker(initial);
            colorDlg.ColorChanged += c =>
            {
                AppConfig.Set(key, $"{c.R:X2}{c.G:X2}{c.B:X2}");
                button.SwatchColor = c;
                Program.hardwareOverlay?.RefreshSettings();
            };
            colorDlg.ShowDialog(this);
        }

        private void InitEqualizer()
        {
            labelEqPresetInner.Text = "Mic preset";
            buttonEqBrowse.Text = "RNNoise DLL...";
            labelEqStrengthTitle.Text = "RNNoise strength";
            labelEqGraceTitle.Text = "Gate hold";

            eqLoading = true;
            comboEqPreset.Items.AddRange(EqualizerApo.Presets);
            comboEqPreset.SelectedItem = EqualizerApo.Preset;
            if (comboEqPreset.SelectedIndex < 0) comboEqPreset.SelectedIndex = 0;
            trackEqStrength.Value = EqualizerApo.Strength;
            labelEqStrength.Text = trackEqStrength.Value + "%";
            trackEqGrace.Value = EqualizerApo.Grace;
            labelEqGrace.Text = trackEqGrace.Value + " s";
            eqLoading = false;

            comboEqPreset.SelectedIndexChanged += (s, e) => ApplyEqualizer(() => EqualizerApo.Preset = (string)comboEqPreset.SelectedItem!);
            trackEqStrength.ValueChanged += (s, e) =>
            {
                labelEqStrength.Text = trackEqStrength.Value + "%";
                ApplyEqualizer(() => AppConfig.Set("eq_rnnoise", trackEqStrength.Value));
            };
            trackEqGrace.ValueChanged += (s, e) =>
            {
                labelEqGrace.Text = trackEqGrace.Value + " s";
                ApplyEqualizer(() => AppConfig.Set("eq_rnnoise_grace", trackEqGrace.Value));
            };
            buttonEqBrowse.Click += BtnEqBrowse_Click;

            RefreshEqStatus();
        }

        private void ApplyEqualizer(Action save)
        {
            if (eqLoading) return;
            save();
            try { EqualizerApo.Apply(); }
            catch (Exception ex) { Logger.WriteLine("EqualizerAPO apply failed: " + ex); }
            RefreshEqStatus();
        }

        private void RefreshEqStatus()
        {
            var (ok, text) = EqualizerApo.Status();
            labelEqStatus.Text = text;
            labelEqStatus.ForeColor = ok ? colorEco : colorTurbo;
        }

        private void BtnEqBrowse_Click(object? sender, EventArgs e)
        {
            // Process has no [STAThread]; the shell file dialog hangs off an STA thread (same as AniMatrixControl).
            // It must not be joined on the UI thread: while the dialog is open the main
            // window would stop pumping and Windows would mark G-Helper as not responding.
            var t = new Thread(() =>
            {
                string? path = null;
                using (var dlg = new OpenFileDialog { Filter = "RNNoise VST2 (*.dll)|*.dll", Title = "Select RNNoise DLL" })
                {
                    if (dlg.ShowDialog() == DialogResult.OK) path = dlg.FileName;
                }

                if (path is null) return;

                try
                {
                    BeginInvoke(() => ApplyEqualizer(() => AppConfig.Set("eq_rnnoise_dll", path)));
                }
                catch (InvalidOperationException) { }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }
    }
}