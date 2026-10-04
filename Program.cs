using DS5Bridge.App;
using System.Reflection;

namespace DS5Bridge;

internal static class Program
{
    /// <summary>The app icon (also used by the tray). Loaded once from the
    /// embedded assets/icon.ico so the tray shows the brand mark.</summary>
    public static Icon AppIcon { get; } = LoadIcon();

    private static Icon LoadIcon()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("DS5Bridge.assets.icon.ico");
            if (s is not null) return new Icon(s);
        }
        catch { /* fall back */ }
        return SystemIcons.Application;
    }

    [STAThread]
    private static void Main()
    {
        // Single instance: a second copy would fight over the virtual device.
        using var mutex = new Mutex(true, "DS5Bridge.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("DS5Bridge 已在运行（请查看系统托盘）。", "DS5Bridge",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
