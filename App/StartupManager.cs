using Microsoft.Win32;

namespace DS5Bridge.App;

/// <summary>Controls launching at Windows sign-in via the per-user Run key.</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(Settings.RunValueName) is string s && s.Length > 0;
    }

    public static void Enable()
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot resolve executable path");
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        key.SetValue(Settings.RunValueName, $"\"{exe}\"");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        key?.DeleteValue(Settings.RunValueName, false);
    }

    public static void Apply(bool enabled)
    {
        if (enabled) Enable();
        else Disable();
    }
}
