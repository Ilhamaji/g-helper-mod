using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GHelper.Helpers;

namespace GHelper.UI
{
    public class AppAutoBoostForm : RForm
    {
        private RCheckBox _checkEnable;
        private ListView _listRules;
        private RButton _buttonAddRunning;
        private RButton _buttonAddFile;
        private RButton _buttonDelete;
        private RComboBox _comboMode;
        private Label _labelMode;
        private List<TargetAppRule> _rules = new();

        private static readonly Dictionary<int, string> BoostModeNames = new()
        {
            { 0, "Disabled" },
            { 1, "Enabled" },
            { 2, "Aggressive" },
            { 3, "Efficient Enabled" },
            { 4, "Efficient Aggressive" },
            { 5, "Aggressive at Guaranteed" },
            { 6, "Efficient Aggressive at Guaranteed" }
        };

        public AppAutoBoostForm()
        {
            Text = "Target App Auto CPU Boost";
            Width = 620;
            Height = 460;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            _checkEnable = new RCheckBox
            {
                Text = "Enable Target App Auto CPU Boost Automation",
                Left = 15,
                Top = 15,
                AutoSize = true,
                Checked = AppAutoBoostManager.IsEnabled
            };
            _checkEnable.CheckedChanged += (s, e) =>
            {
                AppAutoBoostManager.IsEnabled = _checkEnable.Checked;
            };

            _listRules = new ListView
            {
                Left = 15,
                Top = 45,
                Width = 440,
                Height = 350,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false
            };
            _listRules.Columns.Add("Process Name", 140);
            _listRules.Columns.Add("Target Boost Mode", 150);
            _listRules.Columns.Add("Executable Path", 140);
            _listRules.SelectedIndexChanged += ListRules_SelectedIndexChanged;

            _buttonAddRunning = new RButton
            {
                Text = "Add Active App...",
                Left = 465,
                Top = 45,
                Width = 125,
                Height = 32
            };
            _buttonAddRunning.Click += ButtonAddRunning_Click;

            _buttonAddFile = new RButton
            {
                Text = "Browse EXE...",
                Left = 465,
                Top = 85,
                Width = 125,
                Height = 32
            };
            _buttonAddFile.Click += ButtonAddFile_Click;

            _buttonDelete = new RButton
            {
                Text = "Remove Rule",
                Left = 465,
                Top = 125,
                Width = 125,
                Height = 32
            };
            _buttonDelete.Click += ButtonDelete_Click;

            _labelMode = new Label
            {
                Text = "Set Boost Mode:",
                Left = 465,
                Top = 175,
                AutoSize = true
            };

            _comboMode = new RComboBox
            {
                Left = 465,
                Top = 195,
                Width = 125,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var kvp in BoostModeNames)
            {
                _comboMode.Items.Add(new KeyValuePair<int, string>(kvp.Key, kvp.Value));
            }
            _comboMode.DisplayMember = "Value";
            _comboMode.ValueMember = "Key";
            _comboMode.SelectedIndexChanged += ComboMode_SelectedIndexChanged;

            Controls.Add(_checkEnable);
            Controls.Add(_listRules);
            Controls.Add(_buttonAddRunning);
            Controls.Add(_buttonAddFile);
            Controls.Add(_buttonDelete);
            Controls.Add(_labelMode);
            Controls.Add(_comboMode);

            InitTheme(true);
            LoadRules();
        }

        private void LoadRules()
        {
            _rules = AppAutoBoostManager.GetRules();
            RefreshRuleList();
        }

        private void RefreshRuleList()
        {
            _listRules.Items.Clear();
            foreach (var rule in _rules)
            {
                string modeName = BoostModeNames.TryGetValue(rule.BoostMode, out string? name) ? name : rule.BoostMode.ToString();
                ListViewItem item = new ListViewItem(rule.ProcessName);
                item.SubItems.Add(modeName);
                item.SubItems.Add(rule.ExePath);
                item.Tag = rule;
                _listRules.Items.Add(item);
            }
        }

        private void ListRules_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_listRules.SelectedItems.Count > 0 && _listRules.SelectedItems[0].Tag is TargetAppRule rule)
            {
                for (int i = 0; i < _comboMode.Items.Count; i++)
                {
                    if (_comboMode.Items[i] is KeyValuePair<int, string> kvp && kvp.Key == rule.BoostMode)
                    {
                        _comboMode.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        private void ComboMode_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_listRules.SelectedItems.Count > 0 && _listRules.SelectedItems[0].Tag is TargetAppRule rule)
            {
                if (_comboMode.SelectedItem is KeyValuePair<int, string> kvp && rule.BoostMode != kvp.Key)
                {
                    rule.BoostMode = kvp.Key;
                    AppAutoBoostManager.SaveRules(_rules);
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

            var newRule = new TargetAppRule
            {
                ProcessName = procName,
                BoostMode = 2, // Default Aggressive
                ExePath = exePath
            };

            _rules.Add(newRule);
            AppAutoBoostManager.SaveRules(_rules);
            RefreshRuleList();
        }

        private void ButtonDelete_Click(object? sender, EventArgs e)
        {
            if (_listRules.SelectedItems.Count > 0 && _listRules.SelectedItems[0].Tag is TargetAppRule rule)
            {
                _rules.Remove(rule);
                AppAutoBoostManager.SaveRules(_rules);
                RefreshRuleList();
            }
        }
    }
}
