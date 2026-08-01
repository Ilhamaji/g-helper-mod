using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using GHelper.Helpers;

namespace GHelper.UI
{
    public class ProcessInfo
    {
        public string ProcessName { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public int Pid { get; set; }
        public string ExePath { get; set; } = string.Empty;

        public override string ToString()
        {
            return string.IsNullOrEmpty(WindowTitle) ? ProcessName : $"{ProcessName} ({WindowTitle})";
        }
    }

    public class ProcessPickerForm : RForm
    {
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private RTextBox _textSearch;
        private ListBox _listProcesses;
        private RButton _buttonSelect;
        private RButton _buttonCancel;
        private List<ProcessInfo> _allProcesses = new();

        public ProcessInfo? SelectedProcess { get; private set; }

        public ProcessPickerForm()
        {
            Text = "Select Running Application";
            Width = 500;
            Height = 450;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            Label labelSearch = new Label
            {
                Text = "Search:",
                Left = 15,
                Top = 15,
                AutoSize = true
            };

            _textSearch = new RTextBox
            {
                Left = 75,
                Top = 12,
                Width = 390
            };
            _textSearch.TextChanged += (s, e) => FilterList();

            _listProcesses = new ListBox
            {
                Left = 15,
                Top = 45,
                Width = 450,
                Height = 310
            };
            _listProcesses.DoubleClick += (s, e) => ConfirmSelection();

            _buttonSelect = new RButton
            {
                Text = "Select",
                Left = 280,
                Top = 365,
                Width = 90,
                Height = 32
            };
            _buttonSelect.Click += (s, e) => ConfirmSelection();

            _buttonCancel = new RButton
            {
                Text = "Cancel",
                Left = 375,
                Top = 365,
                Width = 90,
                Height = 32
            };
            _buttonCancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

            Controls.Add(labelSearch);
            Controls.Add(_textSearch);
            Controls.Add(_listProcesses);
            Controls.Add(_buttonSelect);
            Controls.Add(_buttonCancel);

            InitTheme(true);
            LoadRunningProcesses();
        }

        private void LoadRunningProcesses()
        {
            _allProcesses.Clear();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            uint myPid = (uint)Process.GetCurrentProcess().Id;

            EnumWindows((hwnd, lParam) =>
            {
                if (!IsWindowVisible(hwnd)) return true;

                StringBuilder titleSb = new StringBuilder(1024);
                GetWindowText(hwnd, titleSb, titleSb.Capacity);
                string title = titleSb.ToString();
                if (string.IsNullOrWhiteSpace(title)) return true;

                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;

                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0 || pid == myPid) return true;

                string exePath = GetExePathFromPid(pid);
                string procName = string.IsNullOrEmpty(exePath) ? string.Empty : Path.GetFileNameWithoutExtension(exePath);

                if (string.IsNullOrEmpty(procName))
                {
                    try { procName = Process.GetProcessById((int)pid).ProcessName; } catch { }
                }

                if (string.IsNullOrEmpty(procName) || seen.Contains(procName)) return true;

                seen.Add(procName);
                _allProcesses.Add(new ProcessInfo
                {
                    ProcessName = procName,
                    WindowTitle = title,
                    Pid = (int)pid,
                    ExePath = exePath
                });

                return true;
            }, IntPtr.Zero);

            _allProcesses.Sort((a, b) => string.Compare(a.ProcessName, b.ProcessName, StringComparison.OrdinalIgnoreCase));
            FilterList();
        }

        private void FilterList()
        {
            string filter = _textSearch.Text.Trim();
            _listProcesses.Items.Clear();

            foreach (var item in _allProcesses)
            {
                if (string.IsNullOrEmpty(filter) ||
                    item.ProcessName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.WindowTitle.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _listProcesses.Items.Add(item);
                }
            }

            if (_listProcesses.Items.Count > 0) _listProcesses.SelectedIndex = 0;
        }

        private void ConfirmSelection()
        {
            if (_listProcesses.SelectedItem is ProcessInfo selected)
            {
                SelectedProcess = selected;
                DialogResult = DialogResult.OK;
            }
        }

        private string GetExePathFromPid(uint pid)
        {
            IntPtr hProcess = IntPtr.Zero;
            try
            {
                hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProcess != IntPtr.Zero)
                {
                    StringBuilder sb = new StringBuilder(512);
                    uint size = (uint)sb.Capacity;
                    if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                    {
                        return sb.ToString();
                    }
                }
            }
            catch { }
            finally
            {
                if (hProcess != IntPtr.Zero) CloseHandle(hProcess);
            }
            return string.Empty;
        }
    }
}
