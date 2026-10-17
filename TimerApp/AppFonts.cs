namespace Timer;

internal static class AppFonts
{
    private static FontFamily UiFontFamily => SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;

    public static Font Ui(float size, FontStyle style = FontStyle.Regular) =>
        new(UiFontFamily, size, style);

    public static Font TimerDisplay(float size, FontStyle style = FontStyle.Regular) =>
        new("Consolas", size, style);
}
