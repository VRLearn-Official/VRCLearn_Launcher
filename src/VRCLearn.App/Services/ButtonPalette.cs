using System.Windows.Media;

namespace VRCLearn.App.Services;

/// <summary>按钮配色表。<c>system</c> 与认不出来的值都是跟随系统，返回 <c>null</c>。</summary>
public static class ButtonPalette
{
    public static readonly Color DangerDefault = Color.FromRgb(0xF4, 0x43, 0x36);

    private const double DangerHueTolerance = 45;

    public static Color? Accent(string palette) => palette switch
    {
        "diamond" => Color.FromRgb(0x00, 0x68, 0xB7),
        "bilibili" => Color.FromRgb(0xFB, 0x72, 0x99),
        "grass" => Color.FromRgb(0x3D, 0xA3, 0x5D),
        _ => null,
    };

    /// <summary>跟随系统、且系统色撞上危险色时，危险色取对侧色相；其余情况就是本来的红。</summary>
    public static Color Danger(string palette, Color accent) =>
        Accent(palette) is null && NearDanger(accent) ? Complement(accent) : DangerDefault;

    private static bool NearDanger(Color color)
    {
        var distance = Math.Abs(Hue(color) - Hue(DangerDefault));

        return Math.Min(distance, 360 - distance) < DangerHueTolerance;
    }

    private static Color Complement(Color color) => FromHsv((Hue(color) + 180) % 360, 0.85, 0.72);

    private static double Hue(Color color)
    {
        double red = color.R / 255.0, green = color.G / 255.0, blue = color.B / 255.0;

        var max = Math.Max(red, Math.Max(green, blue));
        var span = max - Math.Min(red, Math.Min(green, blue));

        if (span == 0)
        {
            return 0;
        }

        var hue = max == red ? (green - blue) / span + (green < blue ? 6 : 0)
            : max == green ? (blue - red) / span + 2
            : (red - green) / span + 4;

        return hue * 60;
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var second = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var offset = value - chroma;

        var (red, green, blue) = (int)(hue / 60) switch
        {
            0 => (chroma, second, 0.0),
            1 => (second, chroma, 0.0),
            2 => (0.0, chroma, second),
            3 => (0.0, second, chroma),
            4 => (second, 0.0, chroma),
            _ => (chroma, 0.0, second),
        };

        return Color.FromRgb(
            (byte)((red + offset) * 255),
            (byte)((green + offset) * 255),
            (byte)((blue + offset) * 255));
    }
}
