using GHelper.Fan;
using GHelper.Gpu.NVidia;
using GHelper.Helpers;
using GHelper.Mode;
using GHelper.UI;
using GHelper.USB;
using PawnIO;
using System.Diagnostics;
using System.Windows.Forms.DataVisualization.Charting;

namespace GHelper
{
    public partial class Fans : RForm
    {

        int curIndex = -1;
        DataPoint? curPoint = null;
        int _kbIndex = 0;
        int _chartTabDirection = 0;

        Series seriesCPU;
        Series seriesGPU;
        Series seriesMid;
        Series seriesXGM;

        static bool gpuVisible = true;
        static bool fanRpm = true;

        static readonly Font _axisFont = new Font("Arial", 7F);

        const int tempMin = 20;
        const int tempMax = 110;

        const int fansMax = 100;

        NvidiaGpuControl? nvControl = null;
        ModeControl modeControl = Program.modeControl;

        FanSensorControl fanSensorControl;

        static int gpuPowerBase = 0;
        static bool isGPUPower => gpuPowerBase > 0;
        static bool clampFanDots = AppConfig.IsClampFanDots();

        public Fans()
        {

            InitializeComponent();

            fanSensorControl = new FanSensorControl(this);

            //float dpi = ControlHelper.GetDpiScale(this).Value;
            //comboModes.Size = new Size(comboModes.Width, (int)dpi * 18);
            comboModes.ClientSize = new Size(comboModes.Width, comboModes.Height - 4);

            Text = Properties.Strings.FansAndPower;
            labelPowerLimits.Text = Properties.Strings.PowerLimits;
            checkApplyPower.Text = Properties.Strings.ApplyPowerLimits;

            labelFans.Text = "BIOS " + Properties.Strings.FanCurves;
            labelBoost.Text = Properties.Strings.CPUBoost;
            buttonReset.Text = Properties.Strings.FactoryDefaults;
            checkApplyFans.Text = Properties.Strings.ApplyFanCurve;

            labelGPU.Text = Properties.Strings.GPUSettings;

            labelGPUCoreTitle.Text = Properties.Strings.GPUCoreClockOffset;
            labelGPUMemoryTitle.Text = Properties.Strings.GPUMemoryClockOffset;
            labelGPUBoostTitle.Text = Properties.Strings.GPUBoost;
            labelGPUTempTitle.Text = Properties.Strings.GPUTempTarget;
            labelGPUPowerTitle.Text = Properties.Strings.GPUPower;

            labelRisky.Text = Properties.Strings.UndervoltingRisky;
            buttonApplyAdvanced.Text = Properties.Strings.Apply;
            checkApplyUV.Text = Properties.Strings.AutoApply;

            buttonCalibrate.Text = Properties.Strings.Calibrate;

            checkFanClamp.Text = Properties.Strings.ClampToGrid;
            labelHysteresisUp.Text = Properties.Strings.HysteresisUp;
            labelHysteresisDown.Text = Properties.Strings.HysteresisDown;
            buttonReadLimits.Text = Properties.Strings.ReadLimits;
            buttonDownload.Text = Properties.Strings.InstallPawnIODriver;

            // Built before InitTheme so the theme pass reaches these controls too; controls
            // created afterwards keep the default light colours.
            BuildBoostGuardPanel();

            InitTheme(true);

            labelTip.Visible = false;
            labelTip.BackColor = Color.Transparent;

            seriesCPU = chartCPU.Series.Add("CPU");
            seriesGPU = chartGPU.Series.Add("GPU");
            seriesMid = chartMid.Series.Add("Mid");
            seriesXGM = chartXGM.Series.Add("XGM");

            seriesCPU.Color = colorStandard;
            seriesGPU.Color = colorTurbo;
            seriesMid.Color = colorEco;
            seriesXGM.Color = Color.Orange;

            chartCPU.Series.Add("Floor").Color = Color.FromArgb(64, colorStandard);
            chartGPU.Series.Add("Floor").Color = Color.FromArgb(64, colorTurbo);
            chartMid.Series.Add("Floor").Color = Color.FromArgb(64, colorEco);

            chartCPU.MouseMove += (sender, e) => ChartCPU_MouseMove(sender, e, AsusFan.CPU);
            chartCPU.MouseUp += ChartCPU_MouseUp;
            chartCPU.MouseLeave += ChartCPU_MouseLeave;

            chartGPU.MouseMove += (sender, e) => ChartCPU_MouseMove(sender, e, AsusFan.GPU);
            chartGPU.MouseUp += ChartCPU_MouseUp;
            chartGPU.MouseLeave += ChartCPU_MouseLeave;

            chartMid.MouseMove += (sender, e) => ChartCPU_MouseMove(sender, e, AsusFan.Mid);
            chartMid.MouseUp += ChartCPU_MouseUp;
            chartMid.MouseLeave += ChartCPU_MouseLeave;

            chartXGM.MouseMove += (sender, e) => ChartCPU_MouseMove(sender, e, AsusFan.XGM);
            chartXGM.MouseUp += ChartCPU_MouseUp;
            chartXGM.MouseLeave += ChartCPU_MouseLeave;

            chartCPU.MouseClick += ChartCPU_MouseClick;
            chartGPU.MouseClick += ChartCPU_MouseClick;
            chartMid.MouseClick += ChartCPU_MouseClick;
            chartXGM.MouseClick += ChartCPU_MouseClick;

            chartCPU.TabStop = true;
            chartGPU.TabStop = true;
            chartMid.TabStop = true;
            chartXGM.TabStop = true;

            chartCPU.PreviewKeyDown += Chart_PreviewKeyDown;
            chartGPU.PreviewKeyDown += Chart_PreviewKeyDown;
            chartMid.PreviewKeyDown += Chart_PreviewKeyDown;
            chartXGM.PreviewKeyDown += Chart_PreviewKeyDown;

            chartCPU.KeyDown += (s, e) => Chart_KeyDown(s, e, AsusFan.CPU);
            chartGPU.KeyDown += (s, e) => Chart_KeyDown(s, e, AsusFan.GPU);
            chartMid.KeyDown += (s, e) => Chart_KeyDown(s, e, AsusFan.Mid);
            chartXGM.KeyDown += (s, e) => Chart_KeyDown(s, e, AsusFan.XGM);

            chartCPU.GotFocus += (s, e) => Chart_GotFocus(s, AsusFan.CPU);
            chartGPU.GotFocus += (s, e) => Chart_GotFocus(s, AsusFan.GPU);
            chartMid.GotFocus += (s, e) => Chart_GotFocus(s, AsusFan.Mid);
            chartXGM.GotFocus += (s, e) => Chart_GotFocus(s, AsusFan.XGM);

            chartCPU.LostFocus += Chart_LostFocus;
            chartGPU.LostFocus += Chart_LostFocus;
            chartMid.LostFocus += Chart_LostFocus;
            chartXGM.LostFocus += Chart_LostFocus;

            buttonReset.Click += ButtonReset_Click;

            trackTotal.Maximum = AsusACPI.MaxTotal;
            trackTotal.Minimum = AsusACPI.MinTotal;

            trackSlow.Maximum = AsusACPI.MaxTotal;
            trackSlow.Minimum = AsusACPI.MinTotal;

            trackCPU.Maximum = AsusACPI.MaxCPU;
            trackCPU.Minimum = AsusACPI.MinCPU;

            trackFast.Maximum = AsusACPI.MaxTotal;
            trackFast.Minimum = AsusACPI.MinTotal;

            trackCrossLoad.Maximum = AsusACPI.MaxCrossLoad;
            trackCrossLoad.Minimum = AsusACPI.MinCrossLoad;

            trackGPUtoCPU.Maximum = AsusACPI.MaxGPUtoCPU;
            trackGPUtoCPU.Minimum = AsusACPI.MinGPUtoCPU;

            trackCPUTemp.Maximum = AsusACPI.MaxCPUTemp;
            trackCPUTemp.Minimum = AsusACPI.MinCPUTemp;

            trackTotal.Scroll += TrackTotal_Scroll;
            trackSlow.Scroll += TrackSlow_Scroll;
            trackFast.Scroll += TrackFast_Scroll;
            trackCPU.Scroll += TrackCPU_Scroll;
            trackCrossLoad.Scroll += TrackCross_Scroll;
            trackGPUtoCPU.Scroll += TrackCross_Scroll;
            trackCPUTemp.Scroll += TrackCross_Scroll;

            trackFast.MouseUp += TrackPower_MouseUp;
            trackCPU.MouseUp += TrackPower_MouseUp;
            trackTotal.MouseUp += TrackPower_MouseUp;
            trackSlow.MouseUp += TrackPower_MouseUp;
            trackCrossLoad.MouseUp += TrackPower_MouseUp;
            trackGPUtoCPU.MouseUp += TrackPower_MouseUp;
            trackCPUTemp.MouseUp += TrackPower_MouseUp;

            trackFast.KeyUp += TrackPower_KeyUp;
            trackCPU.KeyUp += TrackPower_KeyUp;
            trackTotal.KeyUp += TrackPower_KeyUp;
            trackSlow.KeyUp += TrackPower_KeyUp;
            trackCrossLoad.KeyUp += TrackPower_KeyUp;
            trackGPUtoCPU.KeyUp += TrackPower_KeyUp;
            trackCPUTemp.KeyUp += TrackPower_KeyUp;

            checkApplyFans.Click += CheckApplyFans_Click;
            checkApplyPower.Click += CheckApplyPower_Click;

            trackGPUClockLimit.Minimum = NvidiaGpuControl.MinClockLimit;
            trackGPUClockLimit.Maximum = NvidiaGpuControl.MaxClockLimit;

            trackGPUCore.Minimum = NvidiaGpuControl.MinCoreOffset;
            trackGPUCore.Maximum = NvidiaGpuControl.MaxCoreOffset;

            trackGPUMemory.Minimum = NvidiaGpuControl.MinMemoryOffset;
            trackGPUMemory.Maximum = NvidiaGpuControl.MaxMemoryOffset;

            trackGPUBoost.Minimum = AsusACPI.MinGPUBoost;
            trackGPUBoost.Maximum = AsusACPI.MaxGPUBoost;

            trackGPUTemp.Minimum = AsusACPI.MinGPUTemp;
            trackGPUTemp.Maximum = AsusACPI.MaxGPUTemp;

            trackGPUPower.Minimum = AsusACPI.MinGPUPower;
            trackGPUPower.Maximum = AsusACPI.MaxGPUPower;

            trackGPUClockLimit.Scroll += trackGPUClockLimit_Scroll;
            trackGPUCore.Scroll += trackGPU_Scroll;
            trackGPUMemory.Scroll += trackGPU_Scroll;

            trackGPUBoost.Scroll += trackGPUPower_Scroll;
            trackGPUTemp.Scroll += trackGPUPower_Scroll;
            trackGPUPower.Scroll += trackGPUPower_Scroll;

            trackGPUCore.MouseUp += TrackGPUClocks_MouseUp;
            trackGPUMemory.MouseUp += TrackGPUClocks_MouseUp;
            trackGPUClockLimit.MouseUp += TrackGPUClocks_MouseUp;

            trackGPUBoost.MouseUp += TrackGPU_MouseUp;
            trackGPUTemp.MouseUp += TrackGPU_MouseUp;
            trackGPUPower.MouseUp += TrackGPU_MouseUp;

            trackHysteresisUp.Scroll += TrackHysteresis_Scroll;
            trackHysteresisDown.Scroll += TrackHysteresis_Scroll;

            trackHysteresisUp.MouseUp += TrackHysteresis_MouseUp;
            trackHysteresisDown.MouseUp += TrackHysteresis_MouseUp;


            //labelInfo.MaximumSize = new Size(280, 0);
            labelFansResult.Visible = false;


            trackUV.Minimum = CpuInfo.MinCPUUV;
            trackUV.Maximum = CpuInfo.MaxCPUUV;

            trackUViGPU.Minimum = CpuInfo.MinIGPUUV;
            trackUViGPU.Maximum = CpuInfo.MaxIGPUUV;

            trackTemp.Minimum = CpuInfo.MinTemp;
            trackTemp.Maximum = CpuInfo.DefaultTemp;

            comboPowerMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboPowerMode.DataSource = new BindingSource(PowerNative.powerModes, null);
            comboPowerMode.DisplayMember = "Value";
            comboPowerMode.ValueMember = "Key";

            FillModes(false);
            InitAll();
            InitCPU();

            comboBoost.SelectedValueChanged += ComboBoost_Changed;
            comboPowerMode.SelectedValueChanged += ComboPowerMode_Changed;


            comboModes.SelectionChangeCommitted += ComboModes_SelectedValueChanged;
            comboModes.TextChanged += ComboModes_TextChanged;
            comboModes.KeyPress += ComboModes_KeyPress;

            Shown += Fans_Shown;

            buttonAdd.Click += ButtonAdd_Click;
            buttonRemove.Click += ButtonRemove_Click;
            buttonRename.Click += ButtonRename_Click;


            trackUV.Scroll += TrackUV_Scroll;
            trackUViGPU.Scroll += TrackUV_Scroll;
            trackTemp.Scroll += TrackUV_Scroll;

            buttonApplyAdvanced.Click += ButtonApplyAdvanced_Click;
            buttonReadLimits.Click += ButtonReadLimits_Click;

            buttonCPU.BorderColor = colorStandard;
            buttonGPU.BorderColor = colorTurbo;
            buttonAdvanced.BorderColor = Color.Gray;

            buttonCPU.Click += ButtonCPU_Click;
            buttonGPU.Click += ButtonGPU_Click;
            buttonAdvanced.Click += ButtonAdvanced_Click;

            checkApplyUV.Click += CheckApplyUV_Click;

            buttonCalibrate.Click += ButtonCalibrate_Click;

            buttonDownload.Click += ButtonDownload_Click;

            checkFanClamp.Checked = clampFanDots;
            checkFanClamp.Click += CheckFanClamp_Click;

            ToggleNavigation(0);

            if (!Program.acpi.IsSupported(AsusACPI.DevsCPUFanCurve)) buttonCalibrate.Visible = false;

            gpuPowerBase = Program.acpi.DeviceGet(AsusACPI.GPU_BASE);
            panelGPUPower.Visible = isGPUPower;

            FormClosed += Fans_FormClosed;
            Activated  += (_, _) => VisualiseAdvanced();

            BoostGuard.Applied += BoostGuardApplied;
            BoostGuard.StateChanged += BoostGuardStateChanged;

            trackUV.AccessibleName = labelLeftUV.Text;
            trackUViGPU.AccessibleName = labelLeftUViGPU.Text;
            trackTemp.AccessibleName = labelLeftTemp.Text;
            trackGPUCore.AccessibleName = labelGPUCoreTitle.Text;
            trackGPUMemory.AccessibleName = labelGPUMemoryTitle.Text;
            trackGPUBoost.AccessibleName = labelGPUBoostTitle.Text;
            trackGPUTemp.AccessibleName = labelGPUTempTitle.Text;
            trackGPUPower.AccessibleName = labelGPUPowerTitle.Text;
            trackGPUClockLimit.AccessibleName = labelGPUClockLimitTitle.Text;
            trackHysteresisUp.AccessibleName = labelHysteresisUp.Text;
            trackHysteresisDown.AccessibleName = labelHysteresisDown.Text;

            chartCPU.AccessibleName = "CPU fan curve";
            chartGPU.AccessibleName = "GPU fan curve";
            chartMid.AccessibleName = "Mid fan curve";
            chartXGM.AccessibleName = "XG Mobile fan curve";
        }

        private void CheckFanClamp_Click(object? sender, EventArgs e)
        {
            clampFanDots = checkFanClamp.Checked;
            AppConfig.Set("fan_clamp", clampFanDots ? 1 : 0);
        }

        private void ButtonDownload_Click(object? sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://pawnio.eu/") { UseShellExecute = true });
        }

        private void ButtonCalibrate_Click(object? sender, EventArgs e)
        {
            buttonCalibrate.Enabled = false;
            fanSensorControl.StartCalibration();
        }

        private void ChartCPU_MouseClick(object? sender, MouseEventArgs e)
        {
            if (sender is null) return;
            Chart chart = (Chart)sender;

            HitTestResult result = chart.HitTest(e.X, e.Y);

            if ((result.ChartElementType == ChartElementType.AxisLabels || result.ChartElementType == ChartElementType.Axis) && result.Axis == chart.ChartAreas[0].AxisY)
            {
                fanRpm = !fanRpm;
                SetAxis(chartCPU, AsusFan.CPU);
                SetAxis(chartGPU, AsusFan.GPU);
                if (chartMid.Visible) SetAxis(chartMid, AsusFan.Mid);
                if (chartXGM.Visible) SetAxis(chartXGM, AsusFan.XGM);
            }

        }

        private void Fans_FormClosed(object? sender, FormClosedEventArgs e)
        {
            BoostGuard.Applied -= BoostGuardApplied;
            BoostGuard.StateChanged -= BoostGuardStateChanged;

            //Because windows charts seem to eat a lot of memory :(
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        }

        private void CheckApplyUV_Click(object? sender, EventArgs e)
        {
            AppConfig.SetMode("auto_uv", checkApplyUV.Checked ? 1 : 0);
            modeControl.AutoRyzen();
        }

        public void InitAll()
        {
            InitMode();
            InitFans();
            InitPower();
            InitPowerPlan();
            InitUV();
            InitGPU();
        }

        public void InitCPU()
        {
            string name = CpuInfo.Name;
            if (name.Length > 0) Text = Properties.Strings.FansAndPower + " - " + name;
        }

        public void ToggleNavigation(int index = 0)
        {

            SuspendLayout();

            buttonCPU.Activated = false;
            buttonGPU.Activated = false;
            buttonAdvanced.Activated = false;

            panelPower.Visible = false;
            panelGPU.Visible = false;
            panelAdvanced.Visible = false;

            switch (index)
            {
                case 1:
                    buttonGPU.Activated = true;
                    panelGPU.Visible = true;
                    break;
                case 2:
                    buttonAdvanced.Activated = true;
                    panelAdvanced.Visible = true;
                    break;
                default:
                    buttonCPU.Activated = true;
                    panelPower.Visible = true;
                    break;
            }

            ResumeLayout(false);
            PerformLayout();
        }

        private void ButtonAdvanced_Click(object? sender, EventArgs e)
        {
            ToggleNavigation(2);
        }

        private void ButtonGPU_Click(object? sender, EventArgs e)
        {
            ToggleNavigation(1);
        }

        private void ButtonCPU_Click(object? sender, EventArgs e)
        {
            ToggleNavigation(0);
        }

        private void ButtonApplyAdvanced_Click(object? sender, EventArgs e)
        {
            string result = modeControl.SetRyzen(true);
            checkApplyUV.Enabled = true;
            ShowLabelRisky(result);
        }

        private void ButtonReadLimits_Click(object? sender, EventArgs e)
        {
            ShowLabelRisky(modeControl.ReadRyzenLimits());
        }

        private void ShowLabelRisky(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            labelRisky.Text = text;
            labelRisky.Visible = true;
        }

        public void InitUV()
        {

            //if (!ProcessHelper.IsUserAdministrator()) return;

            int cpuUV = Math.Max(trackUV.Minimum, Math.Min(trackUV.Maximum, AppConfig.GetMode("cpu_uv", 0)));
            int igpuUV = Math.Max(trackUViGPU.Minimum, Math.Min(trackUViGPU.Maximum, AppConfig.GetMode("igpu_uv", 0)));

            int temp = AppConfig.GetMode("cpu_temp");
            if (temp < CpuInfo.MinTemp || temp > CpuInfo.DefaultTemp) temp = CpuInfo.DefaultTemp;

            checkApplyUV.Enabled = checkApplyUV.Checked = AppConfig.IsApplyUV();

            trackUV.Value = cpuUV;
            trackUViGPU.Value = igpuUV;
            trackTemp.Value = temp;

            VisualiseAdvanced();

            buttonAdvanced.Visible = CpuInfo.IsAMD;

        }

        private void VisualiseAdvanced()
        {
            bool available = ModeControl.IsPawnAvailable();
            bool installed = available || ModeControl.IsPawnInstalled();

            panelPawnIO.Visible   = installed;
            panelDownload.Visible = !installed;

            if (installed)
            {
                panelTitleAdvanced.Visible = CpuInfo.IsSupportedUV();
                labelRisky.Visible         = CpuInfo.IsSupportedUV();
                panelUV.Visible            = CpuInfo.IsSupportedUV();
                panelUViGPU.Visible        = CpuInfo.IsSupportedUViGPU();
            }

            labelUV.Text     = trackUV.Value.ToString();
            labelUViGPU.Text = trackUViGPU.Value.ToString();

            labelTemp.Text = (trackTemp.Value < CpuInfo.DefaultTemp) ? TempHelper.FormatTemp(trackTemp.Value) : "Default";
        }

        private void AdvancedScroll()
        {
            AppConfig.SetMode("auto_uv", 0);
            checkApplyUV.Enabled = checkApplyUV.Checked = false;

            VisualiseAdvanced();

            AppConfig.SetMode("cpu_temp", trackTemp.Value);
            AppConfig.SetMode("cpu_uv", trackUV.Value);
            AppConfig.SetMode("igpu_uv", trackUViGPU.Value);
        }


        private void TrackUV_Scroll(object? sender, EventArgs e)
        {
            AdvancedScroll();
        }

        private void ComboModes_KeyPress(object? sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == 13) RenameToggle();
        }

        private void ComboModes_TextChanged(object? sender, EventArgs e)
        {
            if (comboModes.DropDownStyle == ComboBoxStyle.DropDownList) return;
            if (!Modes.IsCurrentCustom()) return;
            AppConfig.SetMode("mode_name", comboModes.Text);
        }

        private void RenameToggle()
        {
            if (comboModes.DropDownStyle == ComboBoxStyle.DropDownList)
                comboModes.DropDownStyle = ComboBoxStyle.DropDown;
            else
            {
                var mode = Modes.GetCurrent();
                FillModes();
                comboModes.SelectedValue = mode;
            }
        }

        private void ButtonRename_Click(object? sender, EventArgs e)
        {
            RenameToggle();
        }

        private void ButtonRemove_Click(object? sender, EventArgs e)
        {
            int mode = Modes.GetCurrent();
            if (!Modes.IsCurrentCustom()) return;

            Modes.Remove(mode);
            FillModes();
            modeControl.SetPerformanceMode(AsusACPI.PerformanceBalanced);

        }

        private void FillModes(bool contextMenu = true)
        {
            comboModes.DropDownStyle = ComboBoxStyle.DropDownList;
            comboModes.DataSource = new BindingSource(Modes.GetDictonary(), null);
            comboModes.DisplayMember = "Value";
            comboModes.ValueMember = "Key";
            if (contextMenu) Program.settingsForm.SetContextMenu();
        }

        private void ButtonAdd_Click(object? sender, EventArgs e)
        {
            int mode = Modes.Add();
            FillModes();
            modeControl.SetPerformanceMode(mode);
        }

        public void InitMode()
        {
            int mode = Modes.GetCurrent();
            comboModes.SelectedValue = mode;
            buttonRename.Visible = buttonRemove.Visible = Modes.IsCurrentCustom();
        }

        private void ComboModes_SelectedValueChanged(object? sender, EventArgs e)
        {
            var selectedMode = comboModes.SelectedValue;

            if (selectedMode == null) return;
            if ((int)selectedMode == Modes.GetCurrent()) return;

            Debug.WriteLine(selectedMode);

            modeControl.SetPerformanceMode((int)selectedMode);
        }

        private void TrackGPU_MouseUp(object? sender, MouseEventArgs e)
        {
            modeControl.SetGPUPower();
        }

        private void TrackGPUClocks_MouseUp(object? sender, MouseEventArgs e)
        {
            modeControl.SetGPUClocks(true);
        }

        private void InitGPUPower()
        {
            if (!isGPUPower) return;

            int maxGPUPower = NvidiaSmi.GetMaxGPUPower();
            if (maxGPUPower > 0)
            {
                AsusACPI.MaxGPUPower = maxGPUPower - gpuPowerBase - AsusACPI.MaxGPUBoost;
                trackGPUPower.Minimum = AsusACPI.MinGPUPower;
                trackGPUPower.Maximum = AsusACPI.MaxGPUPower;
            }

            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
                int gpuPowerVar = Program.acpi.DeviceGet(AsusACPI.GPU_POWER);
                Logger.WriteLine($"ReadGPUPower ({Modes.GetCurrentBase()}): {gpuPowerBase} + {gpuPowerVar}");

                int gpu_power = AppConfig.GetMode("gpu_power");
                if (gpu_power < 0) gpu_power = (gpuPowerVar >= 0) ? gpuPowerVar : AsusACPI.MaxGPUPower;

                Invoke(delegate
                {
                    trackGPUPower.Value = Math.Max(Math.Min(gpu_power, AsusACPI.MaxGPUPower), AsusACPI.MinGPUPower);
                    VisualiseGPUSettings();
                });
            });
        }

        public void InitGPU()
        {
            Task.Run(() =>
            {
                if (Program.acpi.DeviceGet(AsusACPI.GPUEco) == 1)
                {
                    Invoke(delegate { gpuVisible = buttonGPU.Visible = false; });
                    return;
                }

                if (HardwareControl.GpuControl is null || !HardwareControl.GpuControl.IsValid) HardwareControl.RecreateGpuControl();

                if (HardwareControl.GpuControl is not NvidiaGpuControl nv)
                {
                    Invoke(delegate { gpuVisible = buttonGPU.Visible = false; });
                    return;
                }

                nvControl = nv;

                try
                {
                    int gpu_boost = AppConfig.GetMode("gpu_boost");
                    int gpu_temp = AppConfig.GetMode("gpu_temp");

                    int core = AppConfig.GetMode("gpu_core");
                    int memory = AppConfig.GetMode("gpu_memory");
                    int clock_limit = AppConfig.GetMode("gpu_clock_limit");

                    if (gpu_boost < 0) gpu_boost = AsusACPI.MaxGPUBoost;
                    if (gpu_temp < 0) gpu_temp = AsusACPI.MaxGPUTemp;

                    if (core == -1) core = 0;
                    if (memory == -1) memory = 0;
                    if (clock_limit == -1) clock_limit = NvidiaGpuControl.MaxClockLimit;

                    string? gpuName = null;

                    if (nvControl is not null)
                    {
                        if (nvControl.GetClocks(out int current_core, out int current_memory))
                        {
                            core = current_core;
                            memory = current_memory;
                        }

                        int _clockLimit = nvControl.GetMaxGPUCLock();

                        if (_clockLimit == 0) clock_limit = NvidiaGpuControl.MaxClockLimit;
                        else if (_clockLimit > 0) clock_limit = _clockLimit;

                        try { gpuName = nvControl.FullName; } catch { }
                    }

                    bool boostVisible = Program.acpi.IsSupported(AsusACPI.PPT_GPUC0);
                    bool tempVisible = Program.acpi.IsSupported(AsusACPI.PPT_GPUC2);

                    Invoke(delegate
                    {
                        gpuVisible = buttonGPU.Visible = true;
                        if (gpuName is not null) labelGPU.Text = gpuName;

                        trackGPUClockLimit.Value = Math.Max(Math.Min(clock_limit, NvidiaGpuControl.MaxClockLimit), NvidiaGpuControl.MinClockLimit);

                        trackGPUCore.Value = Math.Max(Math.Min(core, NvidiaGpuControl.MaxCoreOffset), NvidiaGpuControl.MinCoreOffset);
                        trackGPUMemory.Value = Math.Max(Math.Min(memory, NvidiaGpuControl.MaxMemoryOffset), NvidiaGpuControl.MinMemoryOffset);

                        trackGPUBoost.Value = Math.Max(Math.Min(gpu_boost, AsusACPI.MaxGPUBoost), AsusACPI.MinGPUBoost);
                        trackGPUTemp.Value = Math.Max(Math.Min(gpu_temp, AsusACPI.MaxGPUTemp), AsusACPI.MinGPUTemp);

                        panelGPUBoost.Visible = boostVisible;
                        panelGPUTemp.Visible = tempVisible;
                        panelGPUCore.Visible = panelGPUMemory.Visible = nv.IsClockOffsetSupported;

                        VisualiseGPUSettings();

                        InitGPUPower();
                    });
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(ex.ToString());
                    try { Invoke(delegate { gpuVisible = buttonGPU.Visible = false; }); } catch { }
                }
            });
        }

        private void VisualiseGPUSettings()
        {
            labelGPUCore.Text = $"{trackGPUCore.Value} MHz";
            labelGPUMemory.Text = $"{trackGPUMemory.Value} MHz";

            labelGPUBoost.Text = $"{trackGPUBoost.Value}W";
            labelGPUTemp.Text = TempHelper.FormatTemp(trackGPUTemp.Value);

            if (trackGPUClockLimit.Value >= NvidiaGpuControl.MaxClockLimit)
                labelGPUClockLimit.Text = "Default";
            else
                labelGPUClockLimit.Text = $"{trackGPUClockLimit.Value} MHz";

            labelGPUPower.Text = (gpuPowerBase + trackGPUPower.Value) + "W";

        }

        private static readonly string[] HysteresisLabels = { Properties.Strings.VeryLow, Properties.Strings.Low, Properties.Strings.Medium, Properties.Strings.High, Properties.Strings.VeryHigh };

        private void VisualiseHysteresis()
        {
            labelHysteresisUpValue.Text = HysteresisLabels[trackHysteresisUp.Value - 1];
            labelHysteresisDownValue.Text = HysteresisLabels[trackHysteresisDown.Value - 1];
        }

        private void InitHysteresis()
        {
            var defaults = Program.acpi.GetFanHysteresis();
            if (defaults.up < 0 || defaults.down < 0)
            {
                panelHysteresis.Visible = false;
                return;
            }

            panelHysteresis.Visible = true;

            int up = AppConfig.GetMode("hysteresis_up");
            int down = AppConfig.GetMode("hysteresis_down");

            if (up < 0) up = defaults.up > 0 ? defaults.up : 3;
            if (down < 0) down = defaults.down > 0 ? defaults.down : 3;

            trackHysteresisUp.Value = Math.Clamp(up, trackHysteresisUp.Minimum, trackHysteresisUp.Maximum);
            trackHysteresisDown.Value = Math.Clamp(down, trackHysteresisDown.Minimum, trackHysteresisDown.Maximum);
            VisualiseHysteresis();
        }

        private void TrackHysteresis_Scroll(object? sender, EventArgs e)
        {
            AppConfig.SetMode("hysteresis_up", trackHysteresisUp.Value);
            AppConfig.SetMode("hysteresis_down", trackHysteresisDown.Value);
            VisualiseHysteresis();
        }

        private void TrackHysteresis_MouseUp(object? sender, MouseEventArgs e)
        {
            Program.acpi.SetFanHysteresis(trackHysteresisUp.Value, trackHysteresisDown.Value);
        }


        private void trackGPUClockLimit_Scroll(object? sender, EventArgs e)
        {

            int maxClock = (int)Math.Round((float)trackGPUClockLimit.Value / 5) * 5;

            trackGPUClockLimit.Value = maxClock;
            AppConfig.SetMode("gpu_clock_limit", maxClock);
            VisualiseGPUSettings();
        }

        private void trackGPU_Scroll(object? sender, EventArgs e)
        {
            if (sender is null) return;
            TrackBar track = (TrackBar)sender;
            track.Value = (int)Math.Round((float)track.Value / 5) * 5;

            AppConfig.SetMode("gpu_core", trackGPUCore.Value);
            AppConfig.SetMode("gpu_memory", trackGPUMemory.Value);


            VisualiseGPUSettings();

        }

        private void trackGPUPower_Scroll(object? sender, EventArgs e)
        {
            AppConfig.SetMode("gpu_boost", trackGPUBoost.Value);
            AppConfig.SetMode("gpu_temp", trackGPUTemp.Value);

            if (isGPUPower) AppConfig.SetMode("gpu_power", trackGPUPower.Value);

            VisualiseGPUSettings();
        }

        static string ChartYLabel(int percentage, AsusFan device, string unit = "")
        {
            if (percentage == 0) return "OFF";

            int Min = FanSensorControl.GetFanMin(device);
            int Max = FanSensorControl.GetFanMax(device);

            if (fanRpm)
                return (200 * Math.Floor((float)(Min * 100 + (Max - Min) * percentage) / 200)).ToString() + unit;
            else
                return percentage + "%";
        }

        void SetAxis(Chart chart, AsusFan device)
        {

            chart.ChartAreas[0].AxisY.CustomLabels.Clear();

            for (int i = 0; i <= fansMax; i += 10)
            {
                chart.ChartAreas[0].AxisY.CustomLabels.Add(i - 2, i + 2, ChartYLabel(i, device));
            }

            //chart.ChartAreas[0].AxisY.CustomLabels.Add(fansMax -2, fansMax + 2, Properties.Strings.RPM);
            chart.ChartAreas[0].AxisY.Interval = 10;

            chart.ChartAreas[0].AxisX.CustomLabels.Clear();
            for (int i = tempMin; i <= tempMax; i += 10)
            {
                string xLabel = TempHelper.IsFahrenheit
                    ? Math.Round(TempHelper.CelsiusToFahrenheit(i)).ToString()
                    : i.ToString();
                chart.ChartAreas[0].AxisX.CustomLabels.Add(i - 4.5, i + 4.5, xLabel);
            }
        }

        void SetChart(Chart chart, AsusFan device)
        {

            string title = "";
            string scale = TempHelper.IsFahrenheit ? ", RPM/°F" : ", RPM/°C";

            switch (device)
            {
                case AsusFan.CPU:
                    title = Properties.Strings.FanProfileCPU + scale;
                    break;
                case AsusFan.GPU:
                    title = Properties.Strings.FanProfileGPU + scale;
                    break;
                case AsusFan.Mid:
                    title = Properties.Strings.FanProfileMid + scale;
                    break;
                case AsusFan.XGM:
                    title = "XG Mobile" + scale;
                    break;
            }

            chart.Titles[0].Text = title;

            chart.ChartAreas[0].AxisX.Minimum = tempMin;
            chart.ChartAreas[0].AxisX.Maximum = tempMax;
            chart.ChartAreas[0].AxisX.Interval = 10;

            chart.ChartAreas[0].AxisY.Minimum = 0;
            chart.ChartAreas[0].AxisY.Maximum = fansMax;

            chart.ChartAreas[0].AxisY.LabelStyle.Font = _axisFont;
            chart.ChartAreas[0].AxisX.LabelStyle.Font = _axisFont;
            chart.ChartAreas[0].AxisX.LabelStyle.Angle = 0;

            chart.ChartAreas[0].AxisX.MajorGrid.LineColor = chartGrid;
            chart.ChartAreas[0].AxisY.MajorGrid.LineColor = chartGrid;
            chart.ChartAreas[0].AxisX.LineColor = chartGrid;
            chart.ChartAreas[0].AxisY.LineColor = chartGrid;

            SetAxis(chart, device);

            if (chart.Legends.Count > 0)
                chart.Legends[0].Enabled = false;

            if (device != AsusFan.XGM)
            {
                Series floor = chart.Series[1];
                floor.ChartType = SeriesChartType.Area;
                floor.Points.Clear();

                byte[] floors = Program.acpi.GetFanFloor(device);
                for (int i = 0; i < 8; i++)
                    floor.Points.AddXY(30 + i * 10, floors[i]);
                floor.Points.AddXY(tempMax, floors[7]);
            }

        }

        public void FormPosition()
        {

            if (Height > Program.settingsForm.Height)
            {
                Top = Math.Max(Screen.FromControl(Program.settingsForm).WorkingArea.Top, Program.settingsForm.Top + Program.settingsForm.Height - Height);
            }
            else
            {
                Size = MinimumSize = new Size(0, Program.settingsForm.Height);
                Height = Program.settingsForm.Height;
                Top = Program.settingsForm.Top;
            }

            Left = Program.settingsForm.Left - Width - 5;
        }

        private void Fans_Shown(object? sender, EventArgs e)
        {
            FormPosition();
            LogBoostGuardLayout();
        }


        private void TrackPower_MouseUp(object? sender, MouseEventArgs e)
        {
            Task.Run(() =>
            {
                modeControl.AutoPower(true);
            });
        }


        private void TrackPower_KeyUp(object? sender, KeyEventArgs e)
        {
            Task.Run(() =>
            {
                modeControl.AutoPower(true);
            });
        }

        public void InitPowerPlan()
        {
            int boost = PowerNative.GetCPUBoost();
            if (boost >= 0)
                comboBoost.SelectedIndex = Math.Min(boost, comboBoost.Items.Count - 1);

            InitBoostGuard();

            string powerMode = PowerNative.GetPowerMode();
            bool batterySaver = PowerNative.GetBatterySaverStatus();

            comboPowerMode.Enabled = !batterySaver;

            if (batterySaver)
                comboPowerMode.SelectedIndex = 0;
            else
                comboPowerMode.SelectedValue = powerMode;

        }

        private void ComboPowerMode_Changed(object? sender, EventArgs e)
        {
            string powerMode = (string)comboPowerMode.SelectedValue;
            PowerNative.SetPowerMode(powerMode);

            if (PowerNative.GetDefaultPowerMode(Modes.GetCurrentBase()) != powerMode)
                AppConfig.SetMode("powermode", powerMode);
            else
                AppConfig.RemoveMode("powermode");
        }

        private void ComboBoost_Changed(object? sender, EventArgs e)
        {
            if (AppConfig.GetMode("auto_boost") != comboBoost.SelectedIndex)
            {
                // While the guard is on it owns the boost mode, so only remember the choice
                // here instead of fighting the guard for the setting.
                if (!BoostGuard.IsEnabled)
                    PowerNative.SetCPUBoost(comboBoost.SelectedIndex);
            }
            AppConfig.SetMode("auto_boost", comboBoost.SelectedIndex);
        }

        // ---------------------------------------------------------------------------------
        // Boost guard panel
        //
        // Built in code rather than in the designer on purpose: the block is a stack of
        // rows whose heights depend on the text length and the UI language, and hand-placed
        // controls overlapped as soon as the strings changed. A TableLayoutPanel with
        // AutoSize rows lets every row claim the height it needs.
        // ---------------------------------------------------------------------------------

        private RCheckBox checkBoostGuard = default!;
        private RComboBox comboBoostGuardIdle = default!;
        private RComboBox comboBoostGuardGame = default!;
        private RButton buttonBoostGuardGames = default!;
        private Label labelBoostGuardStatus = default!;
        private bool boostGuardBuilding;



        private static readonly string[] BoostModes =
            { "Disabled", "Enabled", "Aggressive", "Efficient Enabled", "Efficient Aggressive", "Aggressive at Guaranteed", "Efficient at Guaranteed" };

        private void BuildBoostGuardPanel()
        {
            boostGuardBuilding = true;

            // The designer authored this form in 192 DPI units, so WinForms scales every
            // hand-placed control by DeviceDpi / 192. Controls created here in code are not
            // touched by that pass, so they have to apply the same factor or they come out
            // twice the size of their neighbours.
            float scale = DeviceDpi / 192f;

            int Px(int value) => (int)Math.Round(value * scale);
            Font Font(float size, FontStyle style = FontStyle.Regular) => new Font("Segoe UI", size, style);

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 0,
                Margin = new Padding(0),
                Padding = new Padding(0),
                Name = "tableBoostGuard"
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            Label Title(string text)
            {
                var label = new Label
                {
                    Text = text,
                    AutoSize = true,
                    Margin = new Padding(0, Px(2), 0, Px(4)),
                    Font = Font(9F, FontStyle.Bold)
                };
                return label;
            }

            Label Hint(string text)
            {
                var label = new Label
                {
                    Text = text,
                    AutoSize = true,
                    // The column is narrow when the main window is short, so the wrap width
                    // follows the panel instead of a fixed number that would clip the text.
                    MaximumSize = new Size(Math.Max(Px(200), panelBoostGuardHost.Width - Px(24)), 0),
                    Margin = new Padding(0, 0, 0, Px(8))
                };
                return label;
            }

            RCheckBox Check(string text)
            {
                var check = new RCheckBox
                {
                    Text = text,
                    AutoSize = true,
                    BackColor = SystemColors.ControlLight,
                    Padding = new Padding(Px(12), Px(6), Px(12), Px(6)),
                    Margin = new Padding(0, 0, 0, Px(4)),
                    UseVisualStyleBackColor = false,
                    // A CheckBox does not wrap, so a narrow column would clip its label.
                    MaximumSize = new Size(Math.Max(Px(200), panelBoostGuardHost.Width - Px(24)), 0)
                };
                return check;
            }

            RComboBox Combo()
            {
                var combo = new RComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                    Margin = new Padding(0, 0, 0, Px(10))
                };
                combo.Items.AddRange(BoostModes);
                return combo;
            }

            void Add(Control control)
            {
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                table.Controls.Add(control, 0, table.RowCount);
                table.RowCount++;
            }

            Add(Title(Properties.Strings.BoostGuard));

            checkBoostGuard = Check(Properties.Strings.BoostGuardToggle);
            Add(checkBoostGuard);
            Add(Hint(Properties.Strings.BoostGuardHint));

            // The live state sits at the top, under the switch it describes. At the bottom of
            // the column it needed scrolling to see, which defeats the point of a status line.
            labelBoostGuardStatus = new Label
            {
                Text = "",
                AutoSize = true,
                MaximumSize = new Size(Math.Max(Px(200), panelBoostGuardHost.Width - Px(24)), 0),
                Margin = new Padding(0, Px(4), 0, Px(6))
            };
            Add(labelBoostGuardStatus);

            Add(Title(Properties.Strings.BoostGuardIdle));
            comboBoostGuardIdle = Combo();
            comboBoostGuardIdle.AccessibleName = Properties.Strings.BoostGuardIdle;
            Add(comboBoostGuardIdle);

            Add(Title(Properties.Strings.BoostGuardGame));
            comboBoostGuardGame = Combo();
            comboBoostGuardGame.AccessibleName = Properties.Strings.BoostGuardGame;
            Add(comboBoostGuardGame);

            // The only source of detection: nothing is ever added automatically.
            Add(Title(Properties.Strings.BoostGuardGames));

            // The table lives in its own window: the side panel is too narrow for two
            // columns, and a grid squashed in here was unreadable.
            buttonBoostGuardGames = new RButton
            {
                Text = Properties.Strings.BoostGuardGames,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, Px(8))
            };
            buttonBoostGuardGames.Click += (_, _) => OpenGameList();
            Add(buttonBoostGuardGames);
            panelBoostGuardHost.Controls.Clear();
            panelBoostGuardHost.Controls.Add(table);

            checkBoostGuard.Click += (_, _) => ToggleBoostGuard();
            comboBoostGuardIdle.SelectedIndexChanged += (_, _) => SaveBoostGuardMode("boost_guard_idle", comboBoostGuardIdle);
            comboBoostGuardGame.SelectedIndexChanged += (_, _) => SaveBoostGuardMode("boost_guard_game", comboBoostGuardGame);

            boostGuardBuilding = false;
        }

        // Off by default. Prints the measured bounds of every Boost guard control, which is
        // how a clipped row or a control with no height gets found without guessing from a
        // screenshot.
        private void LogBoostGuardLayout()
        {
            if (AppConfig.Get("boost_guard_debug") != 1) return;

            void Walk(Control parent, string indent)
            {
                foreach (Control control in parent.Controls)
                {
                    Logger.WriteLine($"Boost guard layout: {indent}{control.GetType().Name} '{control.Name}' " +
                        $"bounds={control.Left},{control.Top} {control.Width}x{control.Height} " +
                        $"visible={control.Visible} text='{(control.Text.Length > 30 ? control.Text[..30] : control.Text)}'");
                    Walk(control, indent + "  ");
                }
            }

            Logger.WriteLine($"Boost guard layout: host={panelBoostGuardHost.Width}x{panelBoostGuardHost.Height}");
            Walk(panelBoostGuardHost, "");
        }

        private void ToggleBoostGuard()
        {
            if (boostGuardBuilding) return;

            AppConfig.Set("boost_guard", checkBoostGuard.Checked ? 1 : 0);

            if (checkBoostGuard.Checked)
            {
                // Seed both sides from the current setting so enabling the guard never moves
                // the boost mode somewhere the user did not ask for.
                int current = comboBoost.SelectedIndex;
                if (AppConfig.Get("boost_guard_idle") < 0) AppConfig.Set("boost_guard_idle", 0);
                if (AppConfig.Get("boost_guard_game") < 0) AppConfig.Set("boost_guard_game", Math.Max(current, 1));

                BoostGuard.Init();
            }
            else
            {
                BoostGuard.Stop();
            }

            InitBoostGuard();
        }

        private void SaveBoostGuardMode(string key, RComboBox combo)
        {
            if (boostGuardBuilding) return;
            if (!BoostGuard.IsEnabled) return;
            if (AppConfig.Get(key) == combo.SelectedIndex) return;

            AppConfig.Set(key, combo.SelectedIndex);
            BoostGuard.Refresh();
            UpdateBoostGuardStatus();
        }

        private void InitBoostGuard()
        {
            if (checkBoostGuard is null) return;
            boostGuardBuilding = true;
            checkBoostGuard.Checked = BoostGuard.IsEnabled;
            comboBoostGuardIdle.SelectedIndex = Math.Clamp(BoostGuard.IdleBoost, 0, comboBoostGuardIdle.Items.Count - 1);
            comboBoostGuardGame.SelectedIndex = Math.Clamp(BoostGuard.GameBoost, 0, comboBoostGuardGame.Items.Count - 1);
            boostGuardBuilding = false;

            bool enabled = BoostGuard.IsEnabled;
            comboBoostGuardIdle.Enabled = enabled;
            comboBoostGuardGame.Enabled = enabled;
            buttonBoostGuardGames.Enabled = enabled;
            // The plain boost picker is a different control for the same setting, so it is
            // locked while the guard owns that setting.
            comboBoost.Enabled = !enabled;
            UpdateBoostGuardStatus();
        }

        private void OpenGameList()
        {
            if (!BoostGuard.IsEnabled) return;
            using var form = new GameListForm();
            form.ShowDialog(this);
            UpdateBoostGuardStatus();
        }
        private static string BoostName(int boost) =>
            boost >= 0 && boost < BoostModes.Length ? BoostModes[boost] : "?";

        private void UpdateBoostGuardStatus()
        {
            if (labelBoostGuardStatus is null) return;

            // Gray reads as disabled, which is why the idle line was so faint. The idle state
            // is a normal working state, so it uses the ordinary foreground colour; only the
            // guard being off is muted.
            if (!BoostGuard.IsEnabled)
            {
                labelBoostGuardStatus.Text = Properties.Strings.BoostGuardStatusOff;
                labelBoostGuardStatus.ForeColor = colorGray;
                return;
            }

            bool game = BoostGuard.IsGameActive;
            int boost = BoostGuard.AppliedBoost;

            string template = game ? Properties.Strings.BoostGuardStatusGame : Properties.Strings.BoostGuardStatusIdle;
            string text = string.Format(template, BoostName(boost));

            // Naming the process is what tells the user whether detection actually worked.
            string active = BoostGuard.ActiveGame;
            if (game && active.Length > 0) text = text + " (" + active + ")";

            labelBoostGuardStatus.Text = text;
            // The red mode colour does not reach AA contrast on the light theme (3.36:1),
            // so the status keeps the body foreground colour in both themes. The wording
            // already says whether a game is running.
            labelBoostGuardStatus.ForeColor = RForm.foreMain;
        }

        private void BoostGuardApplied()
        {
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                // The guard fires from its own timer thread; the form can be closing at the
                // same moment, and BeginInvoke on a gone handle would throw there.
                if (!IsHandleCreated) return;
                try { BeginInvoke(UpdateBoostGuardStatus); } catch (InvalidOperationException) { }
                return;
            }

            UpdateBoostGuardStatus();
        }

        // The table window is modal, so it never needs a live refresh; only the panel's
        // status line does.
        private void BoostGuardStateChanged()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                if (!IsHandleCreated) return;
                try { BeginInvoke(UpdateBoostGuardStatus); } catch (InvalidOperationException) { }
                return;
            }
            UpdateBoostGuardStatus();
        }
        private void CheckApplyPower_Click(object? sender, EventArgs e)
        {
            if (sender is null) return;
            CheckBox chk = (CheckBox)sender;

            AppConfig.SetMode("auto_apply_power", chk.Checked ? 1 : 0);
            modeControl.SetPerformanceMode();

        }

        private void CheckApplyFans_Click(object? sender, EventArgs e)
        {
            if (sender is null) return;
            CheckBox chk = (CheckBox)sender;

            AppConfig.SetMode("auto_apply", chk.Checked ? 1 : 0);
            modeControl.SetPerformanceMode();

        }

        public void InitAxis()
        {
            if (this == null || this.Text == "") return;

            Invoke(delegate
            {
                buttonCalibrate.Enabled = true;
                SetAxis(chartCPU, AsusFan.CPU);
                SetAxis(chartGPU, AsusFan.GPU);
                if (chartMid.Visible) SetAxis(chartMid, AsusFan.Mid);
            });
        }

        public void LabelFansResult(string text)
        {
            if (text.Length > 0) Logger.WriteLine(text);

            if (this.IsDisposed || !this.IsHandleCreated || this.Text == "") return;

            try { 
                BeginInvoke(delegate
                {
                    labelFansResult.Text = text;
                    labelFansResult.Visible = (text.Length > 0);
                });
            }
            catch (ObjectDisposedException) { }
        }


        public void InitPower()
        {

            bool modeA = Program.acpi.IsSupported(AsusACPI.PPT_APUA0) || CpuInfo.IsAMD;
            bool modeB0 = Program.acpi.IsAllAmdPPT();
            bool modeC1 = Program.acpi.IsSupported(AsusACPI.PPT_APUC1);

            bool modeCross = Program.acpi.IsSupported(AsusACPI.PPT_CROSS9F) || Program.acpi.IsSupported(AsusACPI.PPT_TOTAL9A);
            bool modeGPUtoCPU = Program.acpi.IsSupported(AsusACPI.PPT_GPUCPU9C);
            bool modeCPUTemp = Program.acpi.IsSupported(AsusACPI.PPT_TEMP9E);

            panelTotal.Visible = modeA;
            panelCPU.Visible = modeB0;

            panelCrossLoad.Visible = modeCross;
            panelGPUtoCPU.Visible = modeGPUtoCPU;
            panelCPUTemp.Visible = modeCPUTemp;

            panelApplyPower.Visible = panelTitleCPU.Visible = modeA || modeB0 || modeC1;


            // All AMD version has B0 but doesn't have C0 (Nvidia GPU) settings
            if (modeB0)
            {
                labelLeftTotal.Text = "Platform (CPU + GPU)";
                labelLeftCPU.Text = "CPU";
                panelFast.Visible = false;
                panelSlow.Visible = false;
            }
            else
            {
                panelSlow.Visible = modeA;

                if (CpuInfo.IsAMD)
                {
                    labelLeftTotal.Text = "SPL (CPU sustained)";
                    labelLeftSlow.Text = "sPPT (CPU long boost)";
                    labelLeftFast.Text = "fPPT (CPU short boost)";
                    panelFast.Visible = modeC1;

                }
                else
                {
                    labelLeftTotal.Text = "PL1 (CPU sustained)";
                    labelLeftSlow.Text = "PL2 (CPU long boost)";
                    panelFast.Visible = false;
                }

            }

            checkApplyPower.Checked = AppConfig.IsApplyPower();

            int limit_total = AppConfig.GetMode("limit_total", AsusACPI.DefaultTotal);
            int limit_slow = AppConfig.GetMode("limit_slow", limit_total);
            int limit_fast = AppConfig.GetMode("limit_fast", limit_total);
            int limit_cpu = AppConfig.GetMode("limit_cpu", AsusACPI.DefaultCPU);

            if (limit_total > AsusACPI.MaxTotal) limit_total = AsusACPI.MaxTotal;
            if (limit_total < AsusACPI.MinTotal) limit_total = AsusACPI.MinTotal;

            if (limit_cpu > AsusACPI.MaxCPU) limit_cpu = AsusACPI.MaxCPU;
            if (limit_cpu < AsusACPI.MinCPU) limit_cpu = AsusACPI.MinCPU;

            if (limit_slow > AsusACPI.MaxTotal) limit_slow = AsusACPI.MaxTotal;
            if (limit_slow < AsusACPI.MinTotal) limit_slow = AsusACPI.MinTotal;

            if (limit_fast > AsusACPI.MaxTotal) limit_fast = AsusACPI.MaxTotal;
            if (limit_fast < AsusACPI.MinTotal) limit_fast = AsusACPI.MinTotal;

            int limit_crossload = AppConfig.GetMode("limit_crossload", AsusACPI.MaxCrossLoad);
            int limit_gpucpu = AppConfig.GetMode("limit_gpucpu", AsusACPI.MaxGPUtoCPU);
            int limit_cputemp = AppConfig.GetMode("limit_cputemp", AsusACPI.MaxCPUTemp);

            limit_crossload = Math.Clamp(limit_crossload, AsusACPI.MinCrossLoad, AsusACPI.MaxCrossLoad);
            limit_gpucpu = Math.Clamp(limit_gpucpu, AsusACPI.MinGPUtoCPU, AsusACPI.MaxGPUtoCPU);
            limit_cputemp = Math.Clamp(limit_cputemp, AsusACPI.MinCPUTemp, AsusACPI.MaxCPUTemp);

            trackTotal.Value = limit_total;
            trackSlow.Value = limit_slow;
            trackCPU.Value = limit_cpu;
            trackFast.Value = limit_fast;

            trackCrossLoad.Value = limit_crossload;
            trackGPUtoCPU.Value = limit_gpucpu;
            trackCPUTemp.Value = limit_cputemp;

            trackTotal.AccessibleName = labelLeftTotal.Text;
            trackSlow.AccessibleName = labelLeftSlow.Text;
            trackFast.AccessibleName = labelLeftFast.Text;
            trackCPU.AccessibleName = labelLeftCPU.Text;
            trackCrossLoad.AccessibleName = labelLeftCrossLoad.Text;
            trackGPUtoCPU.AccessibleName = labelLeftGPUtoCPU.Text;
            trackCPUTemp.AccessibleName = labelLeftCPUTemp.Text;

            SavePower();

        }

        private void SavePower()
        {
            labelTotal.Text = trackTotal.Value.ToString() + "W";
            labelSlow.Text = trackSlow.Value.ToString() + "W";
            labelCPU.Text = trackCPU.Value.ToString() + "W";
            labelFast.Text = trackFast.Value.ToString() + "W";

            labelCrossLoad.Text = trackCrossLoad.Value.ToString() + "W";
            labelGPUtoCPU.Text = trackGPUtoCPU.Value.ToString() + "W";
            labelCPUTemp.Text = TempHelper.FormatTemp(trackCPUTemp.Value);

            AppConfig.SetMode("limit_total", trackTotal.Value);
            AppConfig.SetMode("limit_slow", trackSlow.Value);
            AppConfig.SetMode("limit_cpu", trackCPU.Value);
            AppConfig.SetMode("limit_fast", trackFast.Value);

            if (Program.acpi.IsSupported(AsusACPI.PPT_CROSS9F) || Program.acpi.IsSupported(AsusACPI.PPT_TOTAL9A)) AppConfig.SetMode("limit_crossload", trackCrossLoad.Value);
            if (Program.acpi.IsSupported(AsusACPI.PPT_GPUCPU9C)) AppConfig.SetMode("limit_gpucpu", trackGPUtoCPU.Value);
            if (Program.acpi.IsSupported(AsusACPI.PPT_TEMP9E)) AppConfig.SetMode("limit_cputemp", trackCPUTemp.Value);
        }

        private void TrackTotal_Scroll(object? sender, EventArgs e)
        {
            if (trackTotal.Value > trackSlow.Value) trackSlow.Value = trackTotal.Value;
            if (trackTotal.Value > trackFast.Value) trackFast.Value = trackTotal.Value;
            if (trackTotal.Value < trackCPU.Value) trackCPU.Value = trackTotal.Value;
            SavePower();
        }

        private void TrackSlow_Scroll(object? sender, EventArgs e)
        {
            if (trackSlow.Value < trackTotal.Value) trackTotal.Value = trackSlow.Value;
            if (trackSlow.Value > trackFast.Value) trackFast.Value = trackSlow.Value;
            SavePower();
        }

        private void TrackFast_Scroll(object? sender, EventArgs e)
        {
            if (trackFast.Value < trackSlow.Value) trackSlow.Value = trackFast.Value;
            if (trackFast.Value < trackTotal.Value) trackTotal.Value = trackFast.Value;
            SavePower();
        }

        private void TrackCPU_Scroll(object? sender, EventArgs e)
        {
            if (trackCPU.Value > trackTotal.Value) trackTotal.Value = trackCPU.Value;
            SavePower();
        }

        private void TrackCross_Scroll(object? sender, EventArgs e)
        {
            trackGPUtoCPU.Value -= trackGPUtoCPU.Value % AsusACPI.StepGPUtoCPU;
            SavePower();
        }

        public void InitFans()
        {

            int chartCount = 2;

            // Middle / system fan check
            if (!AsusACPI.IsEmptyCurve(Program.acpi.GetFanCurve(AsusFan.Mid)) || Program.acpi.IsMidFanSupported())
            {
                AppConfig.Set("mid_fan", 1);
                chartCount++;
                chartMid.Visible = true;
                SetChart(chartMid, AsusFan.Mid);
                LoadProfile(seriesMid, AsusFan.Mid);
            }
            else
            {
                AppConfig.Set("mid_fan", 0);
            }

            // XG Mobile Fan check
            if (Program.acpi.IsXGConnected() || XGM.IsConnected())
            {
                AppConfig.Set("xgm_fan", 1);
                chartCount++;
                chartXGM.Visible = true;
                SetChart(chartXGM, AsusFan.XGM);
                LoadProfile(seriesXGM, AsusFan.XGM);
            }
            else
            {
                AppConfig.Set("xgm_fan", 0);
            }

            try
            {
                if (chartCount > 2)
                    Size = MinimumSize = new Size(Size.Width, Math.Max(MinimumSize.Height, (int)(ControlHelper.GetDpiScale(this).Value * (chartCount * 200 + 100))));

            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }


            SetChart(chartCPU, AsusFan.CPU);
            SetChart(chartGPU, AsusFan.GPU);

            LoadProfile(seriesCPU, AsusFan.CPU);
            LoadProfile(seriesGPU, AsusFan.GPU);

            bool autoFans = AppConfig.IsApplyPower() && AppConfig.IsFanRequired();
            bool applyFans = AppConfig.IsApplyFans();

            checkApplyFans.Checked = applyFans;

            if (autoFans || applyFans)
            {
                seriesCPU.Color = colorStandard;
                seriesGPU.Color = colorTurbo;
                seriesMid.Color = colorEco;
                seriesXGM.Color = Color.Orange;
            }
            else
            {
                seriesCPU.Color = Color.Gray;
                seriesGPU.Color = Color.Gray;
                seriesMid.Color = Color.Gray;
                seriesXGM.Color = Color.Gray;
            }

            InitHysteresis();

        }


        void LoadProfile(Series series, AsusFan device, bool reset = false)
        {

            series.ChartType = SeriesChartType.Line;
            series.MarkerSize = 10;
            series.MarkerStyle = MarkerStyle.Circle;

            series.Points.Clear();

            byte[] curve = AppConfig.GetFanConfig(device);

            if (reset || AsusACPI.IsInvalidCurve(curve))
            {
                curve = Program.acpi.GetFanCurve(device, Modes.GetCurrentBase());
                Logger.WriteLine($"Default Curve: {device} - {BitConverter.ToString(curve)}");
                if (AsusACPI.IsInvalidCurve(curve))
                    curve = AppConfig.GetDefaultCurve(device);

                curve = AsusACPI.FixFanCurve(curve);

            }

            //Debug.WriteLine(BitConverter.ToString(curve));

            byte old = 0;
            for (int i = 0; i < 8; i++)
            {
                if (curve[i] == old) curve[i]++; // preventing 2 points in same spot from default asus profiles
                series.Points.AddXY(curve[i], curve[i + 8]);
                old = curve[i];
            }

            SaveProfile(series, device);

        }

        void SaveProfile(Series series, AsusFan device)
        {
            byte[] curve = new byte[16];
            int i = 0;
            foreach (DataPoint point in series.Points)
            {
                curve[i] = (byte)Math.Round(point.XValue);
                curve[i + 8] = (byte)point.YValues.First();
                i++;
            }

            AppConfig.SetFanConfig(device, curve);
            //Program.wmi.SetFanCurve(device, curve);

        }

        private void ButtonReset_Click(object? sender, EventArgs e)
        {

            LoadProfile(seriesCPU, AsusFan.CPU, true);
            LoadProfile(seriesGPU, AsusFan.GPU, true);

            if (AppConfig.Is("mid_fan"))
                LoadProfile(seriesMid, AsusFan.Mid, true);

            if (AppConfig.Is("xgm_fan"))
                LoadProfile(seriesXGM, AsusFan.XGM, true);

            checkApplyFans.Checked = false;
            checkApplyPower.Checked = false;
            seriesCPU.Color = Color.Gray;
            seriesGPU.Color = Color.Gray;
            seriesMid.Color = Color.Gray;
            seriesXGM.Color = Color.Gray;

            AppConfig.SetMode("auto_apply", 0);
            AppConfig.SetMode("auto_apply_power", 0);

            trackUV.Value = CpuInfo.MaxCPUUV;
            trackUViGPU.Value = CpuInfo.MaxIGPUUV;
            trackTemp.Value = CpuInfo.DefaultTemp;

            trackCrossLoad.Value = AsusACPI.MaxCrossLoad;
            trackGPUtoCPU.Value = AsusACPI.MaxGPUtoCPU;
            trackCPUTemp.Value = AsusACPI.MaxCPUTemp;
            SavePower();

            AdvancedScroll();
            AppConfig.RemoveMode("cpu_temp");

            modeControl.ResetPerformanceMode();

            InitPowerPlan();

            XGM.Reset();


            if (gpuVisible)
            {
                trackGPUClockLimit.Value = NvidiaGpuControl.MaxClockLimit;
                trackGPUCore.Value = 0;
                trackGPUMemory.Value = 0;

                trackGPUBoost.Value = AsusACPI.MaxGPUBoost;
                trackGPUTemp.Value = AsusACPI.MaxGPUTemp;

                //AppConfig.SetMode("gpu_boost", trackGPUBoost.Value);
                //AppConfig.SetMode("gpu_temp", trackGPUTemp.Value);

                AppConfig.RemoveMode("gpu_boost");
                AppConfig.RemoveMode("gpu_temp");

                AppConfig.RemoveMode("gpu_power");
                AppConfig.RemoveMode("gpu_clock_limit");
                AppConfig.RemoveMode("gpu_core");
                AppConfig.RemoveMode("gpu_memory");

                InitGPUPower();

                VisualiseGPUSettings();
                modeControl.SetGPUClocks(true, true);
                modeControl.SetGPUPower();
            }

            if (panelHysteresis.Visible)
            {
                AppConfig.RemoveMode("hysteresis_up");
                AppConfig.RemoveMode("hysteresis_down");
                InitHysteresis();
            }

        }

        private void Chart_Save()
        {
            curPoint = null;
            curIndex = -1;
            labelTip.Visible = false;

            SaveProfile(seriesCPU, AsusFan.CPU);
            SaveProfile(seriesGPU, AsusFan.GPU);

            if (AppConfig.Is("mid_fan"))
                SaveProfile(seriesMid, AsusFan.Mid);

            if (AppConfig.Is("xgm_fan"))
                SaveProfile(seriesXGM, AsusFan.XGM);

            modeControl.AutoFans();
        }

        private void ChartCPU_MouseUp(object? sender, MouseEventArgs e)
        {
            Chart_Save();
        }


        private void ChartCPU_MouseLeave(object? sender, EventArgs e)
        {
            curPoint = null;
            curIndex = -1;
            labelTip.Visible = false;
            FanDragHint(false);
        }

        private void Chart_PreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
        {
            Chart chart = (Chart)sender!;
            int count = chart.Series[0].Points.Count;

            switch (e.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                case Keys.Home:
                case Keys.End:
                    e.IsInputKey = true;
                    break;
                case Keys.Tab:
                    bool back = e.Modifiers.HasFlag(Keys.Shift);
                    if (back ? _kbIndex > 0 : _kbIndex < count - 1)
                        e.IsInputKey = true;
                    else
                        _chartTabDirection = back ? -1 : 1;
                    break;
            }
        }

        private void Chart_GotFocus(object? sender, AsusFan device)
        {
            Chart chart = (Chart)sender!;
            int count = chart.Series[0].Points.Count;
            if (count == 0) return;

            if (_chartTabDirection > 0) _kbIndex = 0;
            else if (_chartTabDirection < 0) _kbIndex = count - 1;
            else if (_kbIndex < 0 || _kbIndex >= count) _kbIndex = 0;
            _chartTabDirection = 0;

            UpdateChartTip(chart, device);
        }

        private void Chart_LostFocus(object? sender, EventArgs e)
        {
            labelTip.Visible = false;
        }

        private void Chart_KeyDown(object? sender, KeyEventArgs e, AsusFan device)
        {
            Chart chart = (Chart)sender!;
            Series series = chart.Series[0];
            int count = series.Points.Count;
            if (count == 0) return;

            if (_kbIndex < 0 || _kbIndex >= count) _kbIndex = 0;

            int step = e.Shift ? 5 : 1;

            switch (e.KeyCode)
            {
                case Keys.Tab:
                    _kbIndex = Math.Max(0, Math.Min(count - 1, e.Shift ? _kbIndex - 1 : _kbIndex + 1));
                    break;
                case Keys.Home:
                    _kbIndex = 0;
                    break;
                case Keys.End:
                    _kbIndex = count - 1;
                    break;
                case Keys.Left:
                    Chart_AdjustPoint(-step, 0, series, device);
                    break;
                case Keys.Right:
                    Chart_AdjustPoint(step, 0, series, device);
                    break;
                case Keys.Down:
                    Chart_AdjustPoint(0, -step, series, device);
                    break;
                case Keys.Up:
                    Chart_AdjustPoint(0, step, series, device);
                    break;
                default:
                    return;
            }

            UpdateChartTip(chart, device);
            e.Handled = true;
        }

        private void Chart_AdjustPoint(int dx, int dy, Series series, AsusFan device)
        {
            DataPoint point = series.Points[_kbIndex];

            double newX = point.XValue + dx;
            double newY = point.YValues[0] + dy;

            newX = Math.Max(tempMin, Math.Min(tempMax, newX));
            if (clampFanDots)
            {
                double minX = 30 + (_kbIndex * 10);
                double maxX = minX + 9;
                newX = Math.Max(minX, Math.Min(maxX, newX));
            }

            newY = Math.Max(0, Math.Min(fansMax, newY));
            double dymin = (newX - 70) * 1.2;
            if (newY < dymin) newY = dymin;

            point.XValue = newX;
            point.YValues[0] = newY;

            AdjustAllLevels(_kbIndex, newX, newY, series);
            SaveProfile(series, device);
        }

        private void UpdateChartTip(Chart chart, AsusFan device)
        {
            DataPoint point = chart.Series[0].Points[_kbIndex];
            labelTip.Text = TempHelper.FormatTemp(point.XValue) + ", " + ChartYLabel((int)point.YValues[0], device, " " + Properties.Strings.RPM);

            try
            {
                ChartArea ca = chart.ChartAreas[0];
                double pixX = ca.AxisX.ValueToPixelPosition(point.XValue);
                double pixY = ca.AxisY.ValueToPixelPosition(point.YValues[0]);
                labelTip.Top = (int)pixY + chart.Top;
                labelTip.Left = Math.Min(chart.Width - labelTip.Width - 20, (int)pixX - 50);
                labelTip.Visible = true;
            }
            catch { }

            if (chart.Focused)
            {
                try
                {
                    chart.AccessibilityObject.RaiseAutomationNotification(
                        System.Windows.Forms.Automation.AutomationNotificationKind.Other,
                        System.Windows.Forms.Automation.AutomationNotificationProcessing.MostRecent,
                        $"{device} point {_kbIndex + 1}, {labelTip.Text}");
                }
                catch { }
            }
        }

        private void ChartCPU_MouseMove(object? sender, MouseEventArgs e, AsusFan device)
        {

            if (sender is null) return;
            Chart chart = (Chart)sender;

            ChartArea ca = chart.ChartAreas[0];
            Axis ax = ca.AxisX;
            Axis ay = ca.AxisY;

            bool tip = false;

            Series series = chart.Series[0];

            if (!e.Button.HasFlag(MouseButtons.Left) || curPoint == null)
            {
                try
                {
                    HitTestResult? hit = chart.HitTest(e.X, e.Y, false, ChartElementType.DataPoint).FirstOrDefault(h => h.Series == series);
                    if (hit is not null && hit.PointIndex >= 0 && hit.PointIndex < hit.Series.Points.Count)
                    {
                        curIndex = hit.PointIndex;
                        curPoint = hit.Series.Points[curIndex];
                        tip = true;
                    }
                    else if (!e.Button.HasFlag(MouseButtons.Left))
                    {
                        curPoint = null;
                        curIndex = -1;
                    }
                }
                catch
                {
                    curPoint = null;
                    curIndex = -1;
                }
            }
            else
            {
                tip = true;
            }


            if (curPoint != null)
            {

                double dx, dy, dymin;

                try
                {
                    dx = ax.PixelPositionToValue(e.X);
                    dy = ay.PixelPositionToValue(e.Y);

                    if (dx < tempMin) dx = tempMin;
                    if (dx > tempMax) dx = tempMax;

                    if (dy < 0) dy = 0;
                    if (dy > fansMax) dy = fansMax;

                    dymin = (dx - 70) * 1.2;

                    if (dy < dymin) dy = dymin;

                    if (e.Button.HasFlag(MouseButtons.Left))
                    {
                        double deltaY = dy - curPoint.YValues[0];
                        double deltaX = dx - curPoint.XValue;

                        if (clampFanDots)
                        {
                            double minX = 30 + (curIndex * 10);
                            double maxX = minX + 9;
                            dx = Math.Max(minX, Math.Min(maxX, dx));
                        }

                        curPoint.XValue = dx;

                        if (Control.ModifierKeys == Keys.Shift)
                            AdjustAll(0, deltaY, series);
                        else
                        {
                            curPoint.YValues[0] = dy;
                            AdjustAllLevels(curIndex, dx, dy, series);
                        }

                        tip = true;
                    }

                    labelTip.Text = TempHelper.FormatTemp(curPoint.XValue) + ", " + ChartYLabel((int)curPoint.YValues[0], device, " " + Properties.Strings.RPM);
                    labelTip.Top = e.Y + ((Control)sender).Top;
                    labelTip.Left = Math.Min(chart.Width - labelTip.Width - 20, e.X - 50);

                }
                catch
                {
                    Debug.WriteLine(e.Y);
                    tip = false;
                }

            }

            labelTip.Visible = tip;

            FanDragHint(curPoint != null);

        }

        private void FanDragHint(bool show)
        {
            labelFansResult.Text = show ? Properties.Strings.FanDragAll : "";
            labelFansResult.ForeColor = show ? colorGray : colorTurbo;
            labelFansResult.Visible = show;
        }

        private void AdjustAll(double deltaX, double deltaY, Series series)
        {
            for (int i = 0; i < series.Points.Count; i++)
            {
                series.Points[i].XValue = Math.Max(tempMin, Math.Min(tempMax, series.Points[i].XValue + deltaX));
                series.Points[i].YValues[0] = Math.Max(0, Math.Min(100, series.Points[i].YValues[0] + deltaY));
            }
        }

        private void AdjustAllLevels(int index, double curXVal, double curYVal, Series series)
        {

            // Get the neighboring DataPoints of the hit point
            DataPoint? upperPoint = null;
            DataPoint? lowerPoint = null;

            if (index > 0)
            {
                lowerPoint = series.Points[index - 1];
            }

            if (index < series.Points.Count - 1)
            {
                upperPoint = series.Points[index + 1];
            }

            // Adjust the values according to the comparison between the value and its neighbors
            if (upperPoint != null)
            {
                if (curYVal > upperPoint.YValues[0])
                {

                    for (int i = index + 1; i < series.Points.Count; i++)
                    {
                        DataPoint curUpper = series.Points[i];
                        if (curUpper.YValues[0] >= curYVal) break;

                        curUpper.YValues[0] = curYVal;
                    }
                }
                if (curXVal > upperPoint.XValue)
                {

                    for (int i = index + 1; i < series.Points.Count; i++)
                    {
                        DataPoint curUpper = series.Points[i];
                        if (curUpper.XValue >= curXVal) break;

                        curUpper.XValue = curXVal;
                    }
                }
            }

            if (lowerPoint != null)
            {
                //Debug.WriteLine(curYVal + " <? " + Math.Floor(lowerPoint.YValues[0]));
                if (curYVal <= Math.Floor(lowerPoint.YValues[0]))
                {
                    for (int i = index - 1; i >= 0; i--)
                    {
                        DataPoint curLower = series.Points[i];
                        if (curLower.YValues[0] < curYVal) break;
                        curLower.YValues[0] = Math.Floor(curYVal);
                    }
                }
                if (curXVal < lowerPoint.XValue)
                {

                    for (int i = index - 1; i >= 0; i--)
                    {
                        DataPoint curLower = series.Points[i];
                        if (curLower.XValue <= curXVal) break;

                        curLower.XValue = curXVal;
                    }
                }
            }
        }

    }

}
