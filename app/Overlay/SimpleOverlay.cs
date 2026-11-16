using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace GHelper
{
    public class SimpleOverlayForm : Form
    {
        // Config keys
        private const string ShowCpuBoostKey = "overlay_show_cpu_boost";
        private const string ShowRamKey = "overlay_show_ram";
        private const string ShowCpuKey = "overlay_show_cpu";
        private const string ShowGpuKey = "overlay_show_gpu";

        // UI components
        private readonly Label _cpuBoostLabel;
        private readonly Label _ramLabel;
        private readonly Label _cpuLabel;
        private readonly Label _gpuLabel;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly int _cornerRadius = 8;
        private readonly int _marginFromEdge = 8;

        public SimpleOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.Black;
            TransparencyKey = Color.Black;
            ShowInTaskbar = false;
            TopMost = true;

            var font = new Font("Consolas", 12, FontStyle.Regular);

            _cpuBoostLabel = new Label { Font = font, ForeColor = Color.Lime, AutoSize = true };
            _ramLabel = new Label { Font = font, ForeColor = Color.Lime, AutoSize = true };
            _cpuLabel = new Label { Font = font, ForeColor = Color.Lime, AutoSize = true };
            _gpuLabel = new Label { Font = font, ForeColor = Color.Lime, AutoSize = true };

            Controls.Add(_cpuBoostLabel);
            Controls.Add(_ramLabel);
            Controls.Add(_cpuLabel);
            Controls.Add(_gpuLabel);

            _timer = new System.Windows.Forms.Timer { Interval = 1000, Enabled = true };
            _timer.Tick += (s, e) => UpdateValues();

            UpdatePosition();

            LoadConfig();
        }

        private void LoadConfig()
        {
        }

        private void SaveConfig()
        {
        }

        private void UpdatePosition()
        {
            var workArea = Screen.PrimaryScreen!.WorkingArea;
            int x = workArea.Right - Width - _marginFromEdge;
            int y = workArea.Bottom - Height - _marginFromEdge;
            x = Math.Max(0, Math.Min(x, workArea.Right - Width));
            y = Math.Max(0, Math.Min(y, workArea.Bottom - Height));
            Location = new Point(x, y);
        }

        private void UpdateValues()
        {
            bool boostActive = IsCpuBoostActive();
            _cpuBoostLabel.Text = boostActive ? "Boost: ON" : "Boost: OFF";

            long totalRam = 0, freeRam = 0;
            GetMemoryInfo(out totalRam, out freeRam);
            long usedRam = totalRam - freeRam;
            double ramPct = totalRam > 0 ? (double)usedRam / totalRam * 100 : 0;
            _ramLabel.Text = $"RAM: {ramPct:F1}% ({FormatBytes(usedRam)} / {FormatBytes(totalRam)})";

            int cpuUsage = HardwareControl.cpuUsage ?? 0;
            double cpuTemp = HardwareControl.cpuTemp ?? 0;
            _cpuLabel.Text = $"CPU: {cpuUsage}% {FormatTemp(cpuTemp)}";

            int gpuUsage = HardwareControl.gpuUsage ?? 0;
            double gpuTemp = HardwareControl.gpuTemp ?? 0;
            _gpuLabel.Text = $"GPU: {gpuUsage}% {FormatTemp(gpuTemp)}";

            _cpuBoostLabel.Visible = AppConfig.GetBool(ShowCpuBoostKey, true);
            _ramLabel.Visible = AppConfig.GetBool(ShowRamKey, true);
            _cpuLabel.Visible = AppConfig.GetBool(ShowCpuKey, true);
            _gpuLabel.Visible = AppConfig.GetBool(ShowGpuKey, true);
        }

        private static bool IsCpuBoostActive()
        {
            try
            {
                int autoBoost = AppConfig.Get("auto_boost", -1);
                return autoBoost >= 0;
            }
            catch { return false; }
        }

        private static void GetMemoryInfo(out long total, out long free)
        {
            total = 0; free = 0;
            try
            {
                using var mc = new System.Management.ManagementObjectSearcher("Select * from Win32_OperatingSystem");
                foreach (var obj in mc.Get().Cast<System.Management.ManagementBaseObject>())
                {
                    total = (long)obj["TotalVisibleMemorySize"] * 1024;
                    free = (long)obj["FreePhysicalMemory"] * 1024;
                    break;
                }
            }
            catch { }
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double n = bytes;
            int i;
            for (i = 0; i < units.Length - 1 && n >= 1024; i++, n /= 1024);
            return $"{n:F1} {units[i]}";
        }

        private static string FormatTemp(double? celsius)
        {
            if (!celsius.HasValue || celsius.Value <= -273) return " -- ";
            return $"{(int)Math.Round(celsius.Value)}°C";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var brush = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
            using var path = CreateRoundedPath(ClientRectangle, _cornerRadius);
            e.Graphics.FillPath(brush, path);
            using var pen = new Pen(Color.FromArgb(100, 255, 255, 255), 1);
            e.Graphics.DrawPath(pen, path);
        }

        private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int r = radius;
            r = Math.Min(r, Math.Min(rect.Width / 2, rect.Height / 2));
            path.StartFigure();
            path.AddArc(rect.X, rect.Y, r, r, 180, 90);
            path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
            path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
            path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCLBUTTONDOWN = 0xA1;
            const int HTCAPTION = 0x2;

            if (m.Msg == WM_NCLBUTTONDOWN && (int)m.WParam == HTCAPTION)
            {
                ReleaseCapture();
                SendMessage(Handle, 0x112, HTCAPTION, IntPtr.Zero);
            }
            base.WndProc(ref m);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _timer?.Dispose();
            base.OnHandleDestroyed(e);
        }
    }
}