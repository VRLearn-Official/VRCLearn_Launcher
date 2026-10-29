using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VRCLearn.Core.Resources;

namespace VRCLearn.App.Services;

public static class CoverImageScaler
{
    private const int PreviewSize = 320;

    public static byte[] RenderSquare(string sourcePath)
    {
        var source = new BitmapImage();

        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.UriSource = new Uri(sourcePath, UriKind.Absolute);
        source.EndInit();
        source.Freeze();

        var destination = CoverLayout.Destination(source.PixelWidth, source.PixelHeight);

        var size = CoverLayout.TargetSize;
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, size, size));
            context.DrawImage(
                source,
                new Rect(destination.X, destination.Y, destination.Width, destination.Height));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);

        return stream.ToArray();
    }

    public static BitmapImage? Preview(byte[] png)
    {
        try
        {
            var image = new BitmapImage();

            using var stream = new MemoryStream(png);

            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = PreviewSize;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            return image;
        }
        catch (Exception exception) when (exception is IOException or FormatException or NotSupportedException)
        {
            return null;
        }
    }
}
