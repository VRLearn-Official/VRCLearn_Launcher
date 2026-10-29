using System.Globalization;

namespace VRCLearn.App.Services;

public static class SizeText
{
    public static string Format(long bytes) => bytes switch
    {
        >= 1L << 30 => Text(bytes / (double)(1L << 30), "0.##", "GB"),
        >= 1L << 20 => Text(bytes / (double)(1L << 20), "0.#", "MB"),
        >= 1L << 10 => Text(bytes / (double)(1L << 10), "0.#", "KB"),
        _ => $"{bytes.ToString(CultureInfo.InvariantCulture)} B",
    };

    private static string Text(double value, string format, string unit) =>
        $"{value.ToString(format, CultureInfo.InvariantCulture)} {unit}";
}
