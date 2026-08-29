using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GHelper.Helpers;

namespace GHelper.UI
{
    public class CpuAffinityForm : RForm
    {
        private RCheckBox _checkEnable;
        private ListView _listRules;
        private RButton _buttonAddRunning;
        private RButton _buttonAddFile;
        private RButton _buttonDelete;
        private RButton _buttonApply;
        private RComboBox _comboMode;
        private Label _labelMode;
        private Label _labelCustomMask;
        private RComboBox _comboCustomMask;
        private Label _labelTopology;
        private List<AffinityRule> _rules = new();

        private static readonly Dictionary<int, string> AffinityModeNames = new()
        {
            { 0, "All Cores" },
            { 1, "P-Cores Only" },
            { 2, "E-Cores Only" },
            { 3, "Custom Mask" }
        };

        public CpuAffinityForm()
        {
            Text = "CPU Core Affinity Manager";
            Width = 680;
            Height = 590;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            _checkEnable = new RCheckBox
            {
                Text = "Enable Automatic CPU Core Affinity",
                Left = 15,
                Top = 12,
                AutoSize = true,
                Checked = CpuAffinityManager.IsEnabled
            };
            _checkEnable.CheckedChanged += (s, e) =>
            {
                CpuAffinityManager.IsEnabled = _checkEnable.Checked;
                _labelTopology.Enabled = _checkEnable.Checked;
            };

            _labelTopology = new Label
            {
                Text = BuildTopologyText(),
                Left = 15,
                Top = 33,
                AutoSize = true,
                ForeColor = SystemColors.GrayText
            };

            _listRules = new ListView
            {
                Left = 15,
                Top = 58,
                Width = 455,
                Height = 400,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false
            };
            _listRules.Columns.Add("Process Name", 150);
            _listRules.Columns.Add("Affinity", 130);
            _listRules.Columns.Add("Executable Path", 165);
            _listRules.SelectedIndexChanged += ListRules_SelectedIndexChanged;

            _buttonAddRunning = new RButton
            {
                Text = "Add Active App...",
                Left = 480,
                Top = 58,
                Width = 160,
                Height = 32
            };
            _buttonAddRunning.Click += ButtonAddRunning_Click;

            _buttonAddFile = new RButton
            {
                Text = "Browse EXE...",
                Left = 480,
                Top = 98,
                Width = 160,
                Height = 32
            };
            _buttonAddFile.Click += ButtonAddFile_Click;

            _buttonDelete = new RButton
            {
                Text = "Remove Rule",
                Left = 480,
                Top = 138,
                Width = 160,
                Height = 32
            };
            _buttonDelete.Click += ButtonDelete_Click;

            _labelMode = new Label
            {
                Text = "Affinity:",
                Left = 480,
                Top = 180,
                AutoSize = true
            };

            _comboMode = new RComboBox
            {
                Left = 480,
                Top = 198,
                Width = 160,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var kvp in AffinityModeNames)
            {
                _comboMode.Items.Add(new KeyValuePair<int, string>(kvp.Key, kvp.Value));
            }
            _comboMode.DisplayMember = "Value";
            _comboMode.ValueMember = "Key";
            _comboMode.SelectedIndexChanged += ComboMode_SelectedIndexChanged;

            _labelCustomMask = new Label
            {
                Text = "Custom mask (hex):",
                Left = 480,
                Top = 232,
                AutoSize = true,
                Enabled = false
            };

            _comboCustomMask = new RComboBox
            {
                Left = 480,
                Top = 250,
                Width = 160,
                DropDownStyle = ComboBoxStyle.DropDown
            };
            _comboCustomMask.Items.Add("0x" + CpuAffinityManager.AllCoresMask.ToString("X"));
            _comboCustomMask.Items.Add("0x" + CpuAffinityManager.PCoresMask.ToString("X"));
            _comboCustomMask.Items.Add("0x" + CpuAffinityManager.ECoresMask.ToString("X"));
            _comboCustomMask.Text = CpuAffinityManager.AllCoresMask.ToString("X");
            _comboCustomMask.Enabled = false;
            _comboCustomMask.TextChanged += (s, e) => UpdateMaskComboState();

            _buttonApply = new RButton
            {
                Text = "Apply to Running Process",
                Left = 480,
                Top = 305,
                Width = 160,
                Height = 32
            };
            _buttonApply.Click += ButtonApply_Click;

            Controls.Add(_checkEnable);
            Controls.Add(_labelTopology);
            Controls.Add(_listRules);
            Controls.Add(_buttonAddRunning);
            Controls.Add(_buttonAddFile);
            Controls.Add(_buttonDelete);
            Controls.Add(_labelMode);
            Controls.Add(_comboMode);
            Controls.Add(_labelCustomMask);
            Controls.Add(_comboCustomMask);
            Controls.Add(_buttonApply);

            InitTheme(true);
            LoadRules();
        }

        private static string BuildTopologyText()
        {
            int count = CpuAffinityManager.ProcessorCount;
            if (CpuAffinityManager.IsHybridTopologyDetected)
            {
                int pc = System.Numerics.BitOperations.PopCount(CpuAffinityManager.PCoresMask);
                int ec = System.Numerics.BitOperations.PopCount(CpuAffinityManager.ECoresMask);
                return $"Detected: {count} logical cores (P: {pc}, E: {ec})";
            }
            return $"Detected: {count} logical cores (hybrid P/E topology not detected — P/E presets act as all cores)";
        }

        private void UpdateMaskComboState()
        {
            bool custom = _comboMode.SelectedItem is KeyValuePair<int, string> kvp && kvp.Key == CpuAffinityManager.MODE_CUSTOM;
            _labelCustomMask.Enabled = custom;
            _comboCustomMask.Enabled = custom;
        }

        private void LoadRules()
        {
            _rules = CpuAffinityManager.GetRules();
            RefreshRuleList();
        }

        private void RefreshRuleList()
        {
            _listRules.Items.Clear();
            foreach (var rule in _rules)
            {
                string modeName = AffinityModeNames.TryGetValue(rule.AffinityMode, out string? name) ? name : rule.AffinityMode.ToString();
                if (rule.AffinityMode == CpuAffinityManager.MODE_CUSTOM && !string.IsNullOrWhiteSpace(rule.CustomMask))
                    modeName += " (0x" + rule.CustomMask + ")";
                ListViewItem item = new ListViewItem(rule.ProcessName);
                item.SubItems.Add(modeName);
                item.SubItems.Add(rule.ExePath);
                item.Tag = rule;
                _listRules.Items.Add(item);
            }
        }

        private void ListRules_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_listRules.SelectedItems.Count > 0 && _listRules.SelectedItems[0].Tag is AffinityRule rule)
            {
                for (int i = 0; i < _comboMode.Items.Count; i++)
                {
                    if (_comboMode.Items[i] is KeyValuePair<int, string> kvp && kvp.Key == rule.AffinityMode)
                    {
                        _comboMode.SelectedIndex = i;
                        break;
                    }
                }
                if (!string.IsNullOrWhiteSpace(rule.CustomMask))
                    _comboCustomMask.Text = rule.CustomMask;
                UpdateMaskComboState();
            }
        }

        private void ComboMode_SelectedIndexChanged(object? sender, EventArgs e)
        {
            UpdateMaskComboState();
            if (_listRules.SelectedItems.Count > 0 && _listRules.SelectedItems[0].Tag is AffinityRule rule)
            {
                if (_comboMode.SelectedItem is KeyValuePair<int, string> kvp && rule.AffinityMode != kvp.Key)
                {
                    rule.AffinityMode = kvp.Key;
                    if (kvp.Key != CpuAffinityManager.MODE_CUSTOM) rule.CustomMask = string.Empty;
                    else if (string.IsNullOrWhiteSpace(rule.CustomMask)) rule.CustomMask = CpuAffinityManager.AllCoresMask.ToString("X");
                    CpuAffinityManager.SaveRules(_rules);
                    RefreshRuleList();
                }
            }
        }

        private void ButtonAddRunning_Click(object? sender, EventArgs e)
        {
            using var picker = new ProcessPickerForm();
            if (picker.ShowDialog(this) == DialogResult.OK && picker.SelectedProcess != null)
            {
                AddRule(picker.SelectedProcess.ProcessName, picker.SelectedProcess.ExePath);
            }
        }

        private void ButtonAddFile_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                Title = "Select Target Application Executable"
            };

            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                string procName = Path.GetFileNameWithoutExtension(ofd.FileName);
                AddRule(procName, ofd.FileName);
            }
        }

        private void AddRule(string procName, string exePath)
        {
            if (string.IsNullOrWhiteSpace(procName)) return;

            foreach (var r in _rules)
            {
                if (r.ProcessName.Equals(procName, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, $"A rule for '{procName}' already exists.", "Rule Exists", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            var newRule = new AffinityRule
            {
                ProcessName = procName,
                AffinityMode = CpuAffinityManager.MODE_ALL,
                ExePath = exePath
            };

            _rules.Add(newRule);
            CpuAffinityManager.SaveRules(_rules);
            RefreshRuleList();
        }

        private void ButtonDelete_Click(object? sender, EventArgs e)
        {
            if (_listRules.SelectedItems.Count > 0 && _listRules.SelectedItems[0].Tag is AffinityRule rule)
            {
                _rules.Remove(rule);
                CpuAffinityManager.SaveRules(_rules);
                RefreshRuleList();
            }
        }

        private void ButtonApply_Click(object? sender, EventArgs e)
        {
            if (_listRules.SelectedItems.Count == 0 || _listRules.SelectedItems[0].Tag is not AffinityRule rule)
            {
                MessageBox.Show(this, "Select a rule first, or use the running process picker below.", "No Rule Selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ulong mask = CpuAffinityManager.ResolveMask(rule);
            int applied = CpuAffinityManager.ApplyAffinityToProcess(rule.ProcessName, mask);
            MessageBox.Show(this,
                $"Applied mask 0x{mask:X} to '{rule.ProcessName}' ({applied} running instance(s)).",
                "Apply Affinity",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UpdateMaskComboState();
        }
    }
}
