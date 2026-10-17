using Microsoft.Win32;

namespace Timer;

internal sealed record ThemePalette(
    bool Dark,
    Color Accent,
    Color Back,
    Color Surface,
    Color Border,
    Color Fore,
    Color Track,
    Color Secondary,
    Color SecondaryHover,
    Color SecondaryPressed,
    Color SecondaryBorder)
{
    public Color AccentHover => AdjustColorBrightness(Accent, Dark ? 0.12f : 0.08f);
    public Color AccentPressed => AdjustColorBrightness(Accent, Dark ? -0.16f : -0.18f);

    public static ThemePalette FromSystem()
    {
        bool dark = IsSystemDarkTheme();
        return new ThemePalette(
            Dark: dark,
            Accent: GetSystemAccentColor(),
            Back: dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243),
            Surface: dark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(251, 251, 251),
            Border: dark ? Color.FromArgb(72, 72, 72) : Color.FromArgb(224, 224, 224),
            Fore: dark ? Color.FromArgb(245, 245, 245) : Color.FromArgb(32, 32, 32),
            Track: dark ? Color.FromArgb(50, 50, 50) : Color.FromArgb(228, 228, 228),
            Secondary: dark ? Color.FromArgb(59, 59, 59) : Color.FromArgb(255, 255, 255),
            SecondaryHover: dark ? Color.FromArgb(69, 69, 69) : Color.FromArgb(246, 246, 246),
            SecondaryPressed: dark ? Color.FromArgb(52, 52, 52) : Color.FromArgb(238, 238, 238),
            SecondaryBorder: dark ? Color.FromArgb(76, 76, 76) : Color.FromArgb(215, 215, 215));
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            object? value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return value is int intValue && intValue == 0;
        }
        catch
        {
            return true;
        }
    }

    private static Color GetSystemAccentColor()
    {
        Color fallback = Color.FromArgb(0, 95, 184);

        try
        {
            object? value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM",
                "ColorizationColor",
                null);

            if (value is int intValue)
            {
                uint argb = unchecked((uint)intValue);
                byte red = (byte)((argb >> 16) & 0xFF);
                byte green = (byte)((argb >> 8) & 0xFF);
                byte blue = (byte)(argb & 0xFF);
                return Color.FromArgb(red, green, blue);
            }
        }
        catch
        {
        }

        return fallback;
    }

    private static Color AdjustColorBrightness(Color color, float amount)
    {
        int Adjust(int component) =>
            amount >= 0
                ? component + (int)Math.Round((255 - component) * amount)
                : component + (int)Math.Round(component * amount);

        return Color.FromArgb(
            Math.Clamp(Adjust(color.R), 0, 255),
            Math.Clamp(Adjust(color.G), 0, 255),
            Math.Clamp(Adjust(color.B), 0, 255));
    }
}
