using GHelper.Helpers;
using GHelper.Mode;
using GHelper.UI;
using System.Diagnostics;

namespace GHelper
{
    // The Boost guard game table, in its own window: the side panel is barely wide enough
    // for two columns and clipped as soon as a few games were listed.
    public partial class GameListForm : RForm
    {
        static readonly string[] BoostModes =
            { "Disabled", "Enabled", "Aggressive", "Efficient Enabled", "Efficient Aggressive", "Aggressive at Guaranteed", "Efficient at Guaranteed" };

        DataGridView table = default!;
        RComboBox comboRunning = default!;
        RButton buttonAdd = default!;
        RButton buttonRemove = default!;
        Label labelHint = default!;
        bool filling;
        bool building;
        bool pickedByUser;

        public GameListForm()
        {
            Text = Properties.Strings.BoostGuardGames;
            Name = "GameListForm";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            ClientSize = new Size(720, 480);
            MinimumSize = new Size(520, 380);
            Padding = new Padding(10);

            float scale = DeviceDpi / 192f;
            int Px(int value) => (int)Math.Round(value * scale);

            var tableLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tableLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            table = new DataGridView
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, Px(8)),
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AccessibleName = Properties.Strings.BoostGuardGames
            };
            // Sizes follow the font: the 192-DPI design units halved them at 96 DPI and
            // clipped the header text.
            table.RowTemplate.Height = Math.Max(table.Font.Height + 10, 22);
            table.ColumnHeadersHeight = table.RowTemplate.Height + 4;
            table.EnableHeadersVisualStyles = false;

            var colName = new DataGridViewTextBoxColumn
            {
                Name = "game",
                HeaderText = Properties.Strings.BoostGuardGames,
                FillWeight = 55
            };
            var colBoost = new DataGridViewComboBoxColumn
            {
                Name = "boost",
                HeaderText = Properties.Strings.CPUBoost,
                FillWeight = 45,
                FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
            };
            colBoost.Items.Add("Default");
            colBoost.Items.AddRange(BoostModes);
            table.Columns.Add(colName);
            table.Columns.Add(colBoost);

            table.CellValueChanged += (_, e) => Save(e.RowIndex);
            table.CurrentCellDirtyStateChanged += (_, _) =>
            {
                if (table.IsCurrentCellDirty && table.CurrentCell is DataGridViewComboBoxCell)
                    table.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            // A hand-typed cell that fails validation would otherwise pop the framework dialog.
            table.DataError += (_, e) => e.ThrowException = false;

            var addRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0)
            };
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            addRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            comboRunning = new RComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 0, 0, Px(6))
            };
            comboRunning.AccessibleName = Properties.Strings.BoostGuardAddGame;
            comboRunning.DropDown += (_, _) => FillRunningApps();
            comboRunning.SelectedIndexChanged += (_, _) =>
            {
                if (filling) return;
                pickedByUser = true;
                UpdateButtons();
            };
            addRow.Controls.Add(comboRunning, 0, 0);
            addRow.SetColumnSpan(comboRunning, 2);

            buttonAdd = new RButton
            {
                Text = Properties.Strings.BoostGuardAddGame,
                AutoSize = true,
                Margin = new Padding(0, 0, Px(6), 0)
            };
            buttonAdd.Click += (_, _) => AddSelected();
            addRow.Controls.Add(buttonAdd, 0, 1);

            buttonRemove = new RButton
            {
                Text = Properties.Strings.BoostGuardRemoveGame,
                AutoSize = true,
                Margin = new Padding(0)
            };
            buttonRemove.Click += (_, _) => RemoveSelected();
            addRow.Controls.Add(buttonRemove, 1, 1);

            labelHint = new Label
            {
                Text = Properties.Strings.BoostGuardGamesHint,
                AutoSize = true,
                Margin = new Padding(0, Px(8), 0, 0)
            };

            tableLayout.Controls.Add(table, 0, 0);
            tableLayout.Controls.Add(addRow, 0, 1);
            tableLayout.Controls.Add(labelHint, 0, 2);
            Controls.Add(tableLayout);

            InitTheme(true);
            Fill();
            FillRunningApps();
            Shown += (_, _) => FillRunningApps();
        }

        // Rewrites the whole list from the table: keeps the stored order and the per-game
        // picks in step with what the user sees.
        void Save(int _)
        {
            if (building) return;

            var names = new List<string>();
            var modes = new List<(string name, int mode)>();
            foreach (DataGridViewRow row in table.Rows)
            {
                if (row.IsNewRow) continue;
                string name = (row.Cells["game"].Value as string ?? "").Trim();
                if (name.Length == 0) continue;
                names.Add(name);
                int selected = 0;
                if (row.Cells["boost"] as DataGridViewComboBoxCell is { Value: string text })
                    selected = Array.IndexOf(BoostModes, text) + 1;
                modes.Add((name, selected - 1));
            }

            BoostGuard.SetGameList(names);
            foreach (var (name, mode) in modes) BoostGuard.SetGameMode(name, mode);
            Fill();
        }

        // A game without its own pick reads "Default".
        void Fill()
        {
            string? selected = table.CurrentRow is { IsNewRow: false } row
                ? row.Cells["game"].Value as string
                : null;

            building = true;
            table.Rows.Clear();
            foreach (string name in BoostGuard.GameList)
            {
                int mode = BoostGuard.GameMode(name);
                int index = table.Rows.Add(name, mode >= 0 && mode < BoostModes.Length ? BoostModes[mode] : "Default");
                if (string.Equals(name, selected, StringComparison.OrdinalIgnoreCase))
                    table.CurrentCell = table.Rows[index].Cells["game"];
            }
            building = false;

            table.BackgroundColor = RForm.buttonMain;
            table.GridColor = RForm.borderMain;
            table.ColumnHeadersDefaultCellStyle.BackColor = RForm.buttonSecond;
            table.ColumnHeadersDefaultCellStyle.ForeColor = RForm.foreMain;
            table.DefaultCellStyle.BackColor = RForm.buttonMain;
            table.DefaultCellStyle.ForeColor = RForm.foreMain;
            table.DefaultCellStyle.SelectionBackColor = RForm.buttonSecond;
            table.DefaultCellStyle.SelectionForeColor = RForm.foreMain;

            UpdateButtons();
        }

        void FillRunningApps()
        {
            string selected = pickedByUser ? comboRunning.SelectedItem?.ToString() ?? "" : "";
            string active = BoostGuard.ActiveGame;

            var names = new List<string>();
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id == Environment.ProcessId) continue;
                    if (process.MainWindowHandle == IntPtr.Zero) continue;
                    string name = process.ProcessName;
                    if (name.Length == 0) continue;
                    if (ForegroundHelper.IsDesktop(name)) continue;
                    if (names.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    names.Add(name);
                }
                catch
                {
                    // A process can exit between the enumeration and the property read.
                }
                finally
                {
                    process.Dispose();
                }
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);

            if (active.Length > 0)
            {
                int at = names.FindIndex(n => string.Equals(n, active, StringComparison.OrdinalIgnoreCase));
                if (at > 0)
                {
                    names.RemoveAt(at);
                    names.Insert(0, active);
                }
            }

            // A DropDownList cannot show placeholder text, so the prompt is an entry; without
            // it the box sits on an arbitrary app and looks like a detected game.
            if (!(names.Count > 0 || selected.Length > 0)) names.Insert(0, Properties.Strings.BoostGuardPickApp);

            filling = true;
            comboRunning.BeginUpdate();
            comboRunning.Items.Clear();
            comboRunning.Items.AddRange(names.Cast<object>().ToArray());
            comboRunning.EndUpdate();

            // Keep the user's pick; otherwise prefer the detected game over the first entry.
            string preferred = selected.Length > 0 ? selected : active;
            int index = preferred.Length > 0
                ? names.FindIndex(n => string.Equals(n, preferred, StringComparison.OrdinalIgnoreCase))
                : -1;
            comboRunning.SelectedIndex = index >= 0 ? index : 0;
            filling = false;

            UpdateButtons();
        }

        string SelectedRunningApp()
        {
            if (comboRunning.SelectedItem is not string name) return "";
            if (string.Equals(name, Properties.Strings.BoostGuardPickApp, StringComparison.Ordinal)) return "";
            return name;
        }

        void AddSelected()
        {
            // The list can be minutes old by the time the button is pressed.
            FillRunningApps();
            string name = SelectedRunningApp();
            if (name.Length == 0) return;
            BoostGuard.AddGame(name);
            pickedByUser = false;
            Fill();
            FillRunningApps();
        }

        // The row the user points at wins; otherwise the running-app pick.
        string SelectedGame()
        {
            if (table.CurrentRow is { IsNewRow: false } row
                && row.Cells["game"].Value is string fromRow && fromRow.Trim().Length > 0)
                return fromRow.Trim();
            return SelectedRunningApp();
        }

        void RemoveSelected()
        {
            string name = SelectedGame();
            if (name.Length == 0) return;
            BoostGuard.RemoveGame(name);
            pickedByUser = false;
            Fill();
            FillRunningApps();
        }

        void UpdateButtons()
        {
            bool enabled = BoostGuard.IsEnabled;
            buttonAdd.Enabled = enabled && SelectedRunningApp().Length > 0;
            string selected = SelectedGame();
            buttonRemove.Enabled = enabled
                && selected.Length > 0
                && BoostGuard.GameList.Contains(selected, StringComparer.OrdinalIgnoreCase);
            table.Enabled = enabled;
            comboRunning.Enabled = enabled;
        }
    }
}
