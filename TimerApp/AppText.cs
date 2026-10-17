using System.Globalization;

namespace Timer;

internal static class AppText
{
    private static readonly Dictionary<string, string> Ja = new()
    {
        ["AppTitle"] = "Timer",
        ["Start"] = "開始",
        ["Resume"] = "再開",
        ["Pause"] = "一時停止",
        ["Reset"] = "リセット",
        ["TopMost"] = "常に最前面",
        ["TimeUp"] = "時間です",
        ["Ok"] = "OK",
        ["MinutesLeftFormat"] = "あと{0}分"
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["AppTitle"] = "Timer",
        ["Start"] = "Start",
        ["Resume"] = "Resume",
        ["Pause"] = "Pause",
        ["Reset"] = "Reset",
        ["TopMost"] = "Always on top",
        ["TimeUp"] = "Time's up",
        ["Ok"] = "OK",
        ["MinutesLeftFormat"] = "{0} min left"
    };

    private static Dictionary<string, string> Current =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase)
            ? Ja
            : En;

    public static string Get(string key) => Current.TryGetValue(key, out var value) ? value : key;

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
