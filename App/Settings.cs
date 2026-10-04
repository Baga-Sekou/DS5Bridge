using System.Text.Json;

namespace DS5Bridge.App;

public sealed class Settings
{
    public const string RunValueName = "DS5Bridge";

    /// <summary>Start the bridge automatically when the app launches.</summary>
    public bool BridgeEnabled { get; set; } = true;

    /// <summary>Register the app to launch at Windows sign-in.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>When true, the window's X button hides to tray instead of exiting.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Hide the physical GameSir pad from games (HidHide) so only the
    /// virtual DualSense is visible.</summary>
    public bool HidePhysicalPad { get; set; }

    /// <summary>Vibration mode: 0 = raw passthrough, 1 = waveform synthesis.</summary>
    public int HapticMode { get; set; } = 1;

    /// <summary>Carrier preset for synthesis: 0 crisp, 1 soft, 2 strong.</summary>
    public int HapticPreset { get; set; }

    /// <summary>Output gain 0..1.</summary>
    public float HapticIntensity { get; set; } = 1.0f;

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DS5Bridge");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* fall through to defaults */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}
