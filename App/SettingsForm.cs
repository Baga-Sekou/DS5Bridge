namespace DS5Bridge.App;

public sealed class SettingsForm : Form
{
    private readonly Settings _settings;
    private readonly BridgeService _service;

    private readonly CheckBox _bridge = new() { Text = "启用桥接（虚拟 DS5 + 输入转发）", AutoSize = true };
    private readonly CheckBox _startup = new() { Text = "开机自启", AutoSize = true };
    private readonly CheckBox _tray = new() { Text = "点关闭(X)时最小化到托盘，继续运行", AutoSize = true };
    private readonly CheckBox _hide = new() { Text = "隐藏物理手柄（HidHide，⚠ 有卡死风险，默认关）", AutoSize = true };

    private readonly CheckBox _synth = new() { Text = "合成震动（关 = 纯 DS5 震动原样转发）", AutoSize = true };
    private readonly ComboBox _preset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly TrackBar _intensity = new() { Minimum = 0, Maximum = 100, Width = 240, TickFrequency = 10 };
    private readonly Label _intensityVal = new() { AutoSize = true };

    private readonly Label _status = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly ListBox _log = new() { Width = 560, Height = 130, IntegralHeight = false };

    /// <summary>Set true by the tray Exit command so the form really closes.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AllowClose { get; set; }

    public SettingsForm(Settings settings, BridgeService service)
    {
        _settings = settings;
        _service = service;

        Text = "DS5Bridge 设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(600, 470);
        Font = new Font("Microsoft YaHei UI", 9f);

        _bridge.Checked = settings.BridgeEnabled;
        _startup.Checked = settings.StartWithWindows;
        _tray.Checked = settings.CloseToTray;
        _hide.Checked = settings.HidePhysicalPad;

        _synth.Checked = settings.HapticMode == 1;
        _preset.Items.AddRange(new object[] { "清脆（脉冲感）", "柔和（低幅嗡鸣）", "强劲（满幅跳动）" });
        _preset.SelectedIndex = Math.Clamp(settings.HapticPreset, 0, 2);
        _preset.Enabled = _synth.Checked;
        _intensity.Value = (int)(settings.HapticIntensity * 100);
        _intensityVal.Text = $"{_intensity.Value}%";

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(16),
            WrapContents = false,
        };
        layout.Controls.Add(_status);
        layout.Controls.Add(Row(8));
        layout.Controls.Add(_bridge);
        layout.Controls.Add(_startup);
        layout.Controls.Add(_tray);
        layout.Controls.Add(_hide);
        layout.Controls.Add(Row(10));
        layout.Controls.Add(_synth);
        layout.Controls.Add(new Label { Text = "震动预设（合成模式）：", AutoSize = true });
        layout.Controls.Add(_preset);
        layout.Controls.Add(new Label { Text = "震动强度：", AutoSize = true });
        var intRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        intRow.Controls.Add(_intensity);
        intRow.Controls.Add(_intensityVal);
        intRow.Controls.Add(MakeTestButton());
        layout.Controls.Add(intRow);
        layout.Controls.Add(new Label { Text = "日志：", AutoSize = true, Margin = new Padding(3, 12, 3, 3) });
        layout.Controls.Add(_log);

        Controls.Add(layout);

        _bridge.CheckedChanged += (_, _) => Apply();
        _startup.CheckedChanged += (_, _) => Apply();
        _hide.CheckedChanged += (_, _) => Apply();
        _synth.CheckedChanged += (_, _) => { _preset.Enabled = _synth.Checked; ApplyHaptic(); };
        _preset.SelectedIndexChanged += (_, _) => ApplyHaptic();
        _intensity.Scroll += (_, _) =>
        {
            _intensityVal.Text = $"{_intensity.Value}%";
            ApplyHaptic();
        };

        _service.StatusChanged += RefreshStatus;
        _service.Log += AppendLog;

        _ = Handle;
        RefreshStatus();
    }

    private static Label Row(int h) => new() { AutoSize = false, Width = 1, Height = h };

    private Control MakeTestButton()
    {
        var b = new Button { Text = "试震", AutoSize = true, Margin = new Padding(12, 0, 0, 0) };
        b.Click += (_, _) => _service.TestRumble();
        return b;
    }

    private void AppendLog(string line)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(() => AppendLog(line)); } catch { } return; }
        _log.Items.Add(line);
        if (_log.Items.Count > 300) _log.Items.RemoveAt(0);
        _log.TopIndex = _log.Items.Count - 1;
    }

    private void RefreshStatus()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(RefreshStatus); } catch { } return; }
        _status.Text = _service.StatusText();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!AllowClose && e.CloseReason == CloseReason.UserClosing && _tray.Checked)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        Apply(true);
        base.OnFormClosing(e);
    }

    private bool _applying;

    private void ApplyHaptic()
    {
        _settings.HapticMode = _synth.Checked ? 1 : 0;
        _settings.HapticPreset = _preset.SelectedIndex;
        _settings.HapticIntensity = _intensity.Value / 100f;
        _settings.Save();
        _service.ApplyHapticSettings();
    }

    private void Apply(bool force = false)
    {
        if (_applying && !force) return;
        _applying = true;
        try
        {
            var hideBefore = _settings.HidePhysicalPad;

            _settings.BridgeEnabled = _bridge.Checked;
            _settings.CloseToTray = _tray.Checked;
            _settings.HidePhysicalPad = _hide.Checked;

            if (_startup.Checked != _settings.StartWithWindows)
            {
                _settings.StartWithWindows = _startup.Checked;
                StartupManager.Apply(_startup.Checked);
            }
            _settings.Save();

            if (_settings.BridgeEnabled) _service.Start();
            else _service.Stop();

            if (_settings.BridgeEnabled && _settings.HidePhysicalPad != hideBefore)
                _service.ApplyHide(_settings.HidePhysicalPad);
        }
        finally { _applying = false; }
    }

    public void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    /// <summary>Reflect the current HapticMode into the checkbox without
    /// re-saving (used when the tray toggles it).</summary>
    public void SyncHapticFromSettings()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(SyncHapticFromSettings); } catch { } return; }
        _synth.Checked = _settings.HapticMode == 1;
        _preset.Enabled = _synth.Checked;
    }
}
