using System.Text.Json;

// Saved to %AppData%\spen-mouse\settings.json.
sealed class Settings
{
    public float Gain { get; set; } = 400;
    public float Smooth { get; set; } = 0.5f;
    public bool FlipY { get; set; } = true;
    public bool HoldToMove { get; set; }
    public bool StartWithWindows { get; set; }

    static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "spen-mouse", "settings.json");

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException) { }
    }

    // HKCU Run key: the usual way, no admin needed. "--minimized" starts straight into the tray.
    public void ApplyStartup()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (StartWithWindows) key?.SetValue("spen-mouse", $"\"{Environment.ProcessPath}\" --minimized");
        else key?.DeleteValue("spen-mouse", throwOnMissingValue: false);
    }
}
