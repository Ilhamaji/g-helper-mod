using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GHelper.Helpers;

namespace GHelper.UI
{
    public class MicNoiseForm : RForm
    {
        private RCheckBox _checkEnable;
        private Label _labelDevice;
        private RComboBox _comboDevice;
        private RCheckBox _checkRnnoise;

        private Label _labelGateTitle;
        private RTrackBar _trackGate;
        private Label _labelGateValue;

        private Label _labelPreampTitle;
        private RTrackBar _trackPreamp;
        private Label _labelPreampValue;
        private RCheckBox _checkSoftClip;

        private Label _labelPresetTitle;
        private RComboBox _comboPreset;

        private RCheckBox _checkEcho;
        private Label _labelEchoLevelTitle;
        private RTrackBar _trackEchoLevel;
        private Label _labelEchoLevelValue;

        private Label _labelEchoDelayTitle;
        private RTrackBar _trackEchoDelay;
        private Label _labelEchoDelayValue;

        private Label _labelStatus;
        private RButton _buttonApply;

        private List<MicDeviceInfo> _devices = new();

        private static readonly string[] Presets = new string[]
        {
            "Studio Podcast Pro (SM7B Warmth)",
            "Karaoke Vocal Master (Singing Echo Mic)",
            "Podcast Warm & Deep (Radio Voice)",
            "Studio Condenser Crisp (Airy Vocal)",
            "Gamer Streamer Pro (Focus & Keyclick Cut)",
            "Acoustic Vocal & Music (Natural Stage)",
            "Pure AI Studio Suppressor (Flat Ref)",
            "Extreme Noise Isolation (Loud Environment)",
            "Default (Raw Mic Bypass)"
        };

        public MicNoiseForm()
        {
            Text = "Microphone Noise EQ & AI Suppression";
            Width = 480;
            Height = 635;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            _checkEnable = new RCheckBox
            {
                Text = "Enable Microphone Noise Reduction & Equalizer APO",
                Left = 20,
                Top = 15,
                AutoSize = true,
                Checked = AppConfig.Is("mic_noise_enabled")
            };
            _checkEnable.CheckedChanged += (s, e) =>
            {
                AppConfig.Set("mic_noise_enabled", _checkEnable.Checked ? 1 : 0);
                ApplyAndShowStatus();
            };

            _labelDevice = new Label
            {
                Text = "Target Microphone Device:",
                Left = 20,
                Top = 50,
                AutoSize = true
            };

            _comboDevice = new RComboBox
            {
                Left = 20,
                Top = 70,
                Width = 425,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _comboDevice.SelectedIndexChanged += (s, e) =>
            {
                if (_comboDevice.SelectedItem is MicDeviceInfo dev)
                {
                    AppConfig.Set("mic_target_device", dev.Id);
                    ApplyAndShowStatus();
                }
            };

            _checkRnnoise = new RCheckBox
            {
                Text = "RNNoise AI Noise Suppression (Neural Network VST)",
                Left = 20,
                Top = 105,
                AutoSize = true,
                Checked = AppConfig.Is("mic_rnnoise_enabled")
            };
            _checkRnnoise.CheckedChanged += (s, e) =>
            {
                AppConfig.Set("mic_rnnoise_enabled", _checkRnnoise.Checked ? 1 : 0);
                ApplyAndShowStatus();
            };

            int savedGate = AppConfig.Get("mic_gate_threshold") == 0 ? -40 : Math.Clamp(AppConfig.Get("mic_gate_threshold"), -100, 0);

            _labelGateTitle = new Label
            {
                Text = "Volume Noise Gate Threshold:",
                Left = 20,
                Top = 135,
                AutoSize = true
            };

            _labelGateValue = new Label
            {
                Text = $"{savedGate} dB",
                Left = 380,
                Top = 135,
                AutoSize = true,
                TextAlign = ContentAlignment.TopRight
            };

            _trackGate = new RTrackBar
            {
                Left = 20,
                Top = 155,
                Width = 425,
                Minimum = -100,
                Maximum = 0,
                Value = savedGate
            };
            _trackGate.ValueChanged += (s, e) =>
            {
                _labelGateValue.Text = $"{_trackGate.Value} dB";
                AppConfig.Set("mic_gate_threshold", _trackGate.Value);
            };
            _trackGate.MouseUp += (s, e) => ApplyAndShowStatus();

            int savedPreamp = Math.Clamp(AppConfig.Get("mic_preamp_gain"), -20, 30);

            _labelPreampTitle = new Label
            {
                Text = "Preamp & Gain Boost:",
                Left = 20,
                Top = 205,
                AutoSize = true
            };

            _labelPreampValue = new Label
            {
                Text = $"{savedPreamp:+0.0;-0.0;+0.0} dB",
                Left = 380,
                Top = 205,
                AutoSize = true,
                TextAlign = ContentAlignment.TopRight
            };

            _trackPreamp = new RTrackBar
            {
                Left = 20,
                Top = 225,
                Width = 425,
                Minimum = -20,
                Maximum = 30,
                Value = savedPreamp
            };
            _trackPreamp.ValueChanged += (s, e) =>
            {
                _labelPreampValue.Text = $"{_trackPreamp.Value:+0.0;-0.0;+0.0} dB";
                AppConfig.Set("mic_preamp_gain", _trackPreamp.Value);
            };
            _trackPreamp.MouseUp += (s, e) => ApplyAndShowStatus();

            _checkSoftClip = new RCheckBox
            {
                Text = "Anti-Clipping Peak Protection (Prevent Sound Distortion / Pecah)",
                Left = 20,
                Top = 270,
                Width = 425,
                AutoSize = true,
                Checked = AppConfig.Get("mic_softclip_enabled", 1) != 0
            };
            _checkSoftClip.CheckedChanged += (s, e) =>
            {
                AppConfig.Set("mic_softclip_enabled", _checkSoftClip.Checked ? 1 : 0);
                ApplyAndShowStatus();
            };

            _labelPresetTitle = new Label
            {
                Text = "Equalizer Preset Profile:",
                Left = 20,
                Top = 300,
                AutoSize = true
            };

            _comboPreset = new RComboBox
            {
                Left = 20,
                Top = 320,
                Width = 425,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var preset in Presets)
            {
                _comboPreset.Items.Add(preset);
            }
            int savedPreset = Math.Clamp(AppConfig.Get("mic_preset_profile"), 0, Presets.Length - 1);
            _comboPreset.SelectedIndex = savedPreset;
            _comboPreset.SelectedIndexChanged += (s, e) =>
            {
                AppConfig.Set("mic_preset_profile", _comboPreset.SelectedIndex);
                if (_comboPreset.SelectedIndex == 1)
                {
                    _checkEcho.Checked = true;
                    AppConfig.Set("mic_echo_enabled", 1);
                }
                ApplyAndShowStatus();
            };

            _checkEcho = new RCheckBox
            {
                Text = "Karaoke Vocal Echo & Reverb (Mic Singing Echo)",
                Left = 20,
                Top = 360,
                AutoSize = true,
                Checked = AppConfig.Get("mic_echo_enabled", savedPreset == 1 ? 1 : 0) != 0 || savedPreset == 1
            };
            _checkEcho.CheckedChanged += (s, e) =>
            {
                AppConfig.Set("mic_echo_enabled", _checkEcho.Checked ? 1 : 0);
                ApplyAndShowStatus();
            };

            _labelEchoLevelTitle = new Label
            {
                Text = "Echo Intensity Level:",
                Left = 20,
                Top = 390,
                AutoSize = true
            };

            _labelEchoLevelValue = new Label
            {
                Text = $"{Math.Clamp(AppConfig.Get("mic_echo_level", 60), 0, 100)}%",
                Left = 380,
                Top = 390,
                AutoSize = true,
                TextAlign = ContentAlignment.TopRight
            };

            _trackEchoLevel = new RTrackBar
            {
                Left = 20,
                Top = 410,
                Width = 425,
                Minimum = 0,
                Maximum = 100,
                Value = Math.Clamp(AppConfig.Get("mic_echo_level", 60), 0, 100)
            };
            _trackEchoLevel.ValueChanged += (s, e) =>
            {
                _labelEchoLevelValue.Text = $"{_trackEchoLevel.Value}%";
                AppConfig.Set("mic_echo_level", _trackEchoLevel.Value);
            };
            _trackEchoLevel.MouseUp += (s, e) => ApplyAndShowStatus();

            _labelEchoDelayTitle = new Label
            {
                Text = "Echo Delay Time:",
                Left = 20,
                Top = 450,
                AutoSize = true
            };

            _labelEchoDelayValue = new Label
            {
                Text = $"{Math.Clamp(AppConfig.Get("mic_echo_delay", 120), 60, 250)} ms",
                Left = 380,
                Top = 450,
                AutoSize = true,
                TextAlign = ContentAlignment.TopRight
            };

            _trackEchoDelay = new RTrackBar
            {
                Left = 20,
                Top = 470,
                Width = 425,
                Minimum = 60,
                Maximum = 250,
                Value = Math.Clamp(AppConfig.Get("mic_echo_delay", 120), 60, 250)
            };
            _trackEchoDelay.ValueChanged += (s, e) =>
            {
                _labelEchoDelayValue.Text = $"{_trackEchoDelay.Value} ms";
                AppConfig.Set("mic_echo_delay", _trackEchoDelay.Value);
            };
            _trackEchoDelay.MouseUp += (s, e) => ApplyAndShowStatus();

            _labelStatus = new Label
            {
                Text = "Status: Ready",
                Left = 20,
                Top = 515,
                Width = 425,
                Height = 35,
                ForeColor = Color.DarkGray
            };

            _buttonApply = new RButton
            {
                Text = "Apply Now",
                Left = 345,
                Top = 550,
                Width = 100,
                Height = 32
            };
            _buttonApply.Click += (s, e) => ApplyAndShowStatus();

            Controls.Add(_checkEnable);
            Controls.Add(_labelDevice);
            Controls.Add(_comboDevice);
            Controls.Add(_checkRnnoise);
            Controls.Add(_labelGateTitle);
            Controls.Add(_labelGateValue);
            Controls.Add(_trackGate);
            Controls.Add(_labelPreampTitle);
            Controls.Add(_labelPreampValue);
            Controls.Add(_trackPreamp);
            Controls.Add(_checkSoftClip);
            Controls.Add(_labelPresetTitle);
            Controls.Add(_comboPreset);
            Controls.Add(_checkEcho);
            Controls.Add(_labelEchoLevelTitle);
            Controls.Add(_labelEchoLevelValue);
            Controls.Add(_trackEchoLevel);
            Controls.Add(_labelEchoDelayTitle);
            Controls.Add(_labelEchoDelayValue);
            Controls.Add(_trackEchoDelay);
            Controls.Add(_labelStatus);
            Controls.Add(_buttonApply);

            InitTheme(true);
            LoadCaptureDevices();
        }

        private void LoadCaptureDevices()
        {
            _devices = MicNoiseManager.GetCaptureDevices();
            _comboDevice.Items.Clear();
            string savedDeviceId = AppConfig.GetString("mic_target_device") ?? "all";
            int selectedIdx = 0;

            for (int i = 0; i < _devices.Count; i++)
            {
                _comboDevice.Items.Add(_devices[i]);
                if (_devices[i].Id.Equals(savedDeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIdx = i;
                }
            }

            if (_comboDevice.Items.Count > 0)
            {
                _comboDevice.SelectedIndex = selectedIdx;
            }
        }

        private void ApplyAndShowStatus()
        {
            if (!MicNoiseManager.IsApoInstalled())
            {
                _labelStatus.Text = "Status: Equalizer APO not detected. Please install Equalizer APO.";
                if (MessageBox.Show(this, "Equalizer APO is required for Mic Noise EQ & RNNoise AI suppression.\nDo you want to download Equalizer APO now?", "Equalizer APO Missing", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://sourceforge.net/projects/equalizerapo/") { UseShellExecute = true }); } catch { }
                }
                return;
            }
            string status = MicNoiseManager.ApplyMicConfig();
            _labelStatus.Text = "Status: " + status;
        }
    }
}
