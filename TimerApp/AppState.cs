using System.Text.Json;

namespace Timer;

public class AppState
{
    public int ResetDurationSeconds { get; set; }
    public int RemainingSeconds { get; set; }
    public bool Running { get; set; }
    public bool TopMost { get; set; }
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
}

// %APPDATA%\rot1024.Timer\settings.json に保存する。読み書きに失敗しても、アプリは止めない
internal static class AppStateStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "rot1024.Timer",
        "settings.json");

    public static AppState? Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            return JsonSerializer.Deserialize<AppState>(File.ReadAllText(SettingsPath));
        }
        catch
        {
            return null;
        }
    }

    public static void Save(AppState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }
}
