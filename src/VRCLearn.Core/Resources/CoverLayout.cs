namespace VRCLearn.Core.Resources;

public readonly record struct CoverRect(int X, int Y, int Width, int Height);

public static class CoverLayout
{
    public const int TargetSize = 512;

    public static CoverRect Destination(int width, int height)
    {
        var scale = Math.Min((double)TargetSize / width, (double)TargetSize / height);
        var scaledWidth = Math.Max(1, (int)Math.Round(width * scale));
        var scaledHeight = Math.Max(1, (int)Math.Round(height * scale));

        return new CoverRect(
            (TargetSize - scaledWidth) / 2,
            (TargetSize - scaledHeight) / 2,
            scaledWidth,
            scaledHeight);
    }
}
