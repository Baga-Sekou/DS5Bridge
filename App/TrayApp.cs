using DS5Bridge.Core;
using Microsoft.Win32;

namespace DS5Bridge.App;

/// <summary>Tray host. Owns the NotifyIcon, the settings window and the bridge
/// service lifecycle, and is the app's ApplicationContext.</summary>
public sealed class TrayApp : ApplicationContext
{
    private readonly Settings _settings;
    private readonly BridgeService _service;
    private readonly NotifyIcon _icon;
    private readonly SettingsForm _form;
    private readonly ToolStripMenuItem _toggle;
    private readonly ToolStripMenuItem _synth;

    public TrayApp()
    {
        _settings = Settings.Load();
        _service = new BridgeService(_settings);

        _form = new SettingsForm(_settings, _service);

        _toggle = new ToolStripMenuItem("停用桥接", null, (_, _) => ToggleBridge());
        _synth = new ToolStripMenuItem("合成震动", null, (_, _) => ToggleSynth())
        {
            Checked = _settings.HapticMode == 1,
            CheckOnClick = false,
        };
        var show = new ToolStripMenuItem("设置…", null, (_, _) => ShowSettings());
        var autostart = new ToolStripMenuItem("开机自启", null, (_, _) => ToggleAutostart())
        {
            Checked = _settings.StartWithWindows,
            CheckOnClick = false,
        };
        var exit = new ToolStripMenuItem("退出", null, (_, _) => ExitApp());

        var menu = new ContextMenuStrip();
        menu.Items.Add(show);
        menu.Items.Add(_toggle);
        menu.Items.Add(_synth);
        menu.Items.Add(autostart);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        _icon = new NotifyIcon
        {
            Icon = Program.AppIcon,
            Visible = true,
            Text = "DS5Bridge",
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowSettings();
        _icon.Text = Truncate(_service.StatusText());

        _service.StatusChanged += OnStatusChanged;

        // Stop cleanly on logoff/shutdown so the virtual device is torn down
        // deliberately rather than leaving a degraded node behind.
        SystemEvents.SessionEnding += OnSessionEnding;

        if (_settings.BridgeEnabled)
            _service.Start();
        SyncMenu();
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        _service.Stop();
    }

    private void OnStatusChanged()
    {
        if (_form.InvokeRequired) { _form.BeginInvoke(SyncMenu); return; }
        SyncMenu();
    }

    private void SyncMenu()
    {
        _toggle.Text = _service.Running ? "停用桥接" : "启用桥接";
        _synth.Checked = _settings.HapticMode == 1;
        _icon.Text = Truncate("DS5Bridge - " + _service.StatusText());
    }

    private void ToggleBridge()
    {
        if (_service.Running) _service.Stop();
        else _service.Start();

        _settings.BridgeEnabled = _service.Running;
        _settings.Save();
        SyncMenu();
    }

    private void ToggleSynth()
    {
        _settings.HapticMode = _settings.HapticMode == 1 ? 0 : 1;
        _settings.Save();
        _service.ApplyHapticSettings();
        _form.SyncHapticFromSettings();
        SyncMenu();
    }

    private void ToggleAutostart()
    {
        _settings.StartWithWindows = !_settings.StartWithWindows;
        StartupManager.Apply(_settings.StartWithWindows);
        _settings.Save();

        // reflect in the settings form checkbox
        if (_form.Visible) _form.ShowWindow();
        SyncMenu();
    }

    private void ShowSettings() => _form.ShowWindow();

    private void ExitApp()
    {
        SystemEvents.SessionEnding -= OnSessionEnding;
        _form.AllowClose = true;
        _service.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _form.Dispose();
        ExitThread();
    }

    private static string Truncate(string s) => s.Length <= 63 ? s : s[..60] + "...";
}
