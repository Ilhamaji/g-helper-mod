using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GHelper.Helpers;

namespace GHelper.UI
{
    public class EcoQosForm : RForm
    {
        private RCheckBox _checkEnable;
        private Label _labelPerAppHeader;
        private ListView _listAppRules;
        private RButton _buttonAppAddRunning;
        private RButton _buttonAppAddFile;
        private RButton _buttonAppDelete;
        private RComboBox _comboAppToggle;
        private Label _labelGlobalHeader;
        private RCheckBox _checkGlobal;
        private RCheckBox _checkGameMode;
        private ListView _listGlobal;
        private RTextBox _textGlobalAdd;
        private RButton _buttonGlobalAdd;
        private RButton _buttonGlobalRemove;
        private Label _labelNote;
        private List<EcoQosRule> _rules = new();
        private List<string> _global = new();

        public EcoQosForm()
        {
            Text = "EcoQoS Manager (Energy-Efficient QoS)";
            Width = 720;
            Height = 640;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            string note = EcoQosManager.IsEcoQoSAvailable
                ? "EcoQoS reduces power & heat of selected processes. Requires Windows 10 1809+."
                : "EcoQoS requires Windows 10 1809 or later — unavailable on this system.";

            _labelNote = new Label
            {
                Text = note,
                Left = 15,
                Top = 10,
                AutoSize = true,
                ForeColor = EcoQosManager.IsEcoQoSAvailable ? SystemColors.GrayText : Color.Firebrick
            };

            _checkEnable = new RCheckBox
            {
                Text = "Enable EcoQoS Automation",
                Left = 15,
                Top = 30,
                AutoSize = true,
                Checked = EcoQosManager.IsEnabled
            };
            _checkEnable.CheckedChanged += (s, e) =>
            {
                EcoQosManager.IsEnabled = _checkEnable.Checked;
            };

            // ── Per-application section ──────────────────────────────────────
            _labelPerAppHeader = new Label
            {
                Text = "Per-Application Rules",
                Font = new Font(Font, FontStyle.Bold),
                Left = 15,
                Top = 56,
                AutoSize = true
            };

            _listAppRules = new ListView
            {
                Left = 15,
                Top = 78,
                Width = 380,
                Height = 250,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false
            };
            _listAppRules.Columns.Add("Process Name", 150);
            _listAppRules.Columns.Add("EcoQoS", 80);
            _listAppRules.Columns.Add("Executable Path", 140);
            _listAppRules.SelectedIndexChanged += ListAppRules_SelectedIndexChanged;

            _buttonAppAddRunning = new RButton
            {
                Text = "Add Active App...",
                Left = 405,
                Top = 78,
                Width = 150,
                Height = 32
            };
            _buttonAppAddRunning.Click += ButtonAppAddRunning_Click;

            _buttonAppAddFile = new RButton
            {
                Text = "Browse EXE...",
                Left = 405,
                Top = 118,
                Width = 150,
                Height = 32
            };
            _buttonAppAddFile.Click += ButtonAppAddFile_Click;

            _buttonAppDelete = new RButton
            {
                Text = "Remove Rule",
                Left = 405,
                Top = 158,
                Width = 150,
                Height = 32
            };
            _buttonAppDelete.Click += ButtonAppDelete_Click;

            Label labelAppToggle = new Label
            {
                Text = "EcoQoS:",
                Left = 405,
                Top = 200,
                AutoSize = true
            };

            _comboAppToggle = new RComboBox
            {
                Left = 405,
                Top = 218,
                Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _comboAppToggle.Items.Add("Eco ON");
            _comboAppToggle.Items.Add("Eco OFF");
            _comboAppToggle.SelectedIndex = 0;
            _comboAppToggle.SelectedIndexChanged += ComboAppToggle_SelectedIndexChanged;

            // ── Global / preset section ──────────────────────────────────────
            _labelGlobalHeader = new Label
            {
                Text = "Global / Preset Background Processes",
                Font = new Font(Font, FontStyle.Bold),
                Left = 15,
                Top = 342,
                AutoSize = true
            };

            _checkGlobal = new RCheckBox
            {
                Text = "Force EcoQoS on all listed background processes",
                Left = 15,
                Top = 362,
                AutoSize = true,
                Checked = EcoQosManager.IsGlobalEnabled
            };
            _checkGlobal.CheckedChanged += (s, e) => EcoQosManager.IsGlobalEnabled = _checkGlobal.Checked;

            _checkGameMode = new RCheckBox
            {
                Text = "Game mode: apply only while an app is active (avoid hurting background downloads)",
                Left = 15,
                Top = 392,
                AutoSize = true,
                Checked = EcoQosManager.IsGameModeEnabled
            };
            _checkGameMode.CheckedChanged += (s, e) => EcoQosManager.IsGameModeEnabled = _checkGameMode.Checked;

            _listGlobal = new ListView
            {
                Left = 15,
                Top = 422,
                Width = 380,
                Height = 130,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false
            };
            _listGlobal.Columns.Add("Process Name", 370);
            _listGlobal.SelectedIndexChanged += ListGlobal_SelectedIndexChanged;

            _textGlobalAdd = new RTextBox
            {
                Left = 405,
                Top = 422,
                Width = 150,
                Height = 30
            };
            _textGlobalAdd.PlaceholderText = "Process name";

            _buttonGlobalAdd = new RButton
            {
                Text = "Add",
                Left = 565,
                Top = 422,
                Width = 60,
                Height = 30
            };
            _buttonGlobalAdd.Click += ButtonGlobalAdd_Click;

            _buttonGlobalRemove = new RButton
            {
                Text = "Remove Selected",
                Left = 405,
                Top = 460,
                Width = 220,
                Height = 30
            };
            _buttonGlobalRemove.Click += ButtonGlobalRemove_Click;

            Controls.Add(_labelNote);
            Controls.Add(_checkEnable);
            Controls.Add(_labelPerAppHeader);
            Controls.Add(_listAppRules);
            Controls.Add(_buttonAppAddRunning);
            Controls.Add(_buttonAppAddFile);
            Controls.Add(_buttonAppDelete);
            Controls.Add(labelAppToggle);
            Controls.Add(_comboAppToggle);
            Controls.Add(_labelGlobalHeader);
            Controls.Add(_checkGlobal);
            Controls.Add(_checkGameMode);
            Controls.Add(_listGlobal);
            Controls.Add(_textGlobalAdd);
            Controls.Add(_buttonGlobalAdd);
            Controls.Add(_buttonGlobalRemove);

            InitTheme(true);
            LoadData();
        }

        private void LoadData()
        {
            _rules = EcoQosManager.GetRules();
            _global = EcoQosManager.GetGlobalProcesses();
            RefreshAppRules();
            RefreshGlobalList();
        }

        private void RefreshAppRules()
        {
            _listAppRules.Items.Clear();
            foreach (var rule in _rules)
            {
                ListViewItem item = new ListViewItem(rule.ProcessName);
                item.SubItems.Add(rule.EcoEnabled ? "Eco ON" : "Eco OFF");
                item.SubItems.Add(rule.ExePath);
                item.Tag = rule;
                _listAppRules.Items.Add(item);
            }
        }

        private void RefreshGlobalList()
        {
            _listGlobal.Items.Clear();
            foreach (var name in _global)
            {
                ListViewItem item = new ListViewItem(name);
                item.Tag = name;
                _listGlobal.Items.Add(item);
            }
        }

        private void ListAppRules_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_listAppRules.SelectedItems.Count > 0 && _listAppRules.SelectedItems[0].Tag is EcoQosRule rule)
            {
                _comboAppToggle.SelectedIndex = rule.EcoEnabled ? 0 : 1;
            }
        }

        private void ComboAppToggle_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_listAppRules.SelectedItems.Count > 0 && _listAppRules.SelectedItems[0].Tag is EcoQosRule rule)
            {
                bool eco = _comboAppToggle.SelectedIndex == 0;
                if (rule.EcoEnabled != eco)
                {
                    rule.EcoEnabled = eco;
                    EcoQosManager.SaveRules(_rules);
                    RefreshAppRules();
                }
            }
        }

        private void ButtonAppAddRunning_Click(object? sender, EventArgs e)
        {
            using var picker = new ProcessPickerForm();
            if (picker.ShowDialog(this) == DialogResult.OK && picker.SelectedProcess != null)
            {
                AddAppRule(picker.SelectedProcess.ProcessName, picker.SelectedProcess.ExePath);
            }
        }

        private void ButtonAppAddFile_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                Title = "Select Target Application Executable"
            };

            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                string procName = Path.GetFileNameWithoutExtension(ofd.FileName);
                AddAppRule(procName, ofd.FileName);
            }
        }

        private void AddAppRule(string procName, string exePath)
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

            _rules.Add(new EcoQosRule { ProcessName = procName, EcoEnabled = true, ExePath = exePath });
            EcoQosManager.SaveRules(_rules);
            RefreshAppRules();
        }

        private void ButtonAppDelete_Click(object? sender, EventArgs e)
        {
            if (_listAppRules.SelectedItems.Count > 0 && _listAppRules.SelectedItems[0].Tag is EcoQosRule rule)
            {
                _rules.Remove(rule);
                EcoQosManager.SaveRules(_rules);
                RefreshAppRules();
            }
        }

        private void ListGlobal_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_listGlobal.SelectedItems.Count > 0 && _listGlobal.SelectedItems[0].Tag is string name)
            {
                _textGlobalAdd.Text = name;
            }
        }

        private void ButtonGlobalAdd_Click(object? sender, EventArgs e)
        {
            string name = _textGlobalAdd.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            foreach (var n in _global)
            {
                if (n.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, $"'{name}' is already in the list.", "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            _global.Add(name);
            EcoQosManager.SaveGlobalProcesses(_global);
            RefreshGlobalList();
        }

        private void ButtonGlobalRemove_Click(object? sender, EventArgs e)
        {
            if (_listGlobal.SelectedItems.Count > 0 && _listGlobal.SelectedItems[0].Tag is string name)
            {
                _global.Remove(name);
                EcoQosManager.SaveGlobalProcesses(_global);
                RefreshGlobalList();
            }
        }
    }
}
