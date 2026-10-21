using System.Buffers;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageResizer;

public enum OutputFormat { Jpg, Webp }

public sealed record ResizeOptions(double Scale, int Quality, OutputFormat Format, bool KeepExif)
{
    public string Extension => Format == OutputFormat.Webp ? ".webp" : ".jpg";
}

static class ImageProcessor
{
    const string JpegOrientation = "/app1/ifd/{ushort=274}";
    const string TiffOrientation = "/ifd/{ushort=274}";

    public static void Resize(string input, string output, ResizeOptions options)
    {
        using var fs = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None).Frames[0];
        var metadata = ReadMetadata(frame);
        var rotation = GetRotation(metadata);

        var source = Scale(frame, fs, options.Scale);
        if (rotation != 0) source = new TransformedBitmap(source, new RotateTransform(rotation));

        try
        {
            if (options.Format == OutputFormat.Webp)
            {
                SaveWebp(source, output, options.Quality);
                return;
            }
            var opaque = HasAlpha(frame)
                ? FlattenOnWhite(source, frame.DpiX, frame.DpiY)
                : new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
            SaveJpeg(opaque, output, options.Quality, options.KeepExif ? CopyExif(metadata, rotation) : null);
        }
        catch
        {
            TryDelete(output);
            throw;
        }
    }

    static BitmapMetadata? ReadMetadata(BitmapFrame frame)
    {
        try { return frame.Metadata as BitmapMetadata; }
        catch { return null; }
    }

    static double GetRotation(BitmapMetadata? metadata)
    {
        var query = metadata?.Format switch { "jpg" => JpegOrientation, "tiff" => TiffOrientation, _ => null };
        if (query is null) return 0;
        try
        {
            return metadata!.GetQuery(query) is ushort o ? o switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 } : 0;
        }
        catch { return 0; }
    }

    static BitmapMetadata? CopyExif(BitmapMetadata? metadata, double rotation)
    {
        if (metadata?.Format != "jpg") return null;
        try
        {
            var exif = metadata.Clone();
            if (rotation != 0) exif.SetQuery(JpegOrientation, (ushort)1);
            return exif;
        }
        catch { return null; }
    }

    static BitmapSource Scale(BitmapFrame frame, Stream stream, double scale)
    {
        if (scale >= 1.0) return frame;
        stream.Position = 0;
        var img = new BitmapImage();
        img.BeginInit();
        img.StreamSource = stream;
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.CreateOptions = BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreImageCache;
        img.DecodePixelWidth = Math.Max(1, (int)Math.Round(frame.PixelWidth * scale));
        img.DecodePixelHeight = Math.Max(1, (int)Math.Round(frame.PixelHeight * scale));
        img.EndInit();
        return img;
    }

    static bool HasAlpha(BitmapSource s)
    {
        var f = s.Format;
        if (f == PixelFormats.Bgra32 || f == PixelFormats.Pbgra32 || f == PixelFormats.Rgba64 || f == PixelFormats.Prgba64
            || f == PixelFormats.Rgba128Float || f == PixelFormats.Prgba128Float) return true;
        return s.Palette?.Colors.Any(c => c.A < 255) == true;
    }

    static byte[] RentBgra(BitmapSource source, out int width, out int height)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        width = bgra.PixelWidth;
        height = bgra.PixelHeight;
        var pixels = ArrayPool<byte>.Shared.Rent(checked(width * height * 4));
        bgra.CopyPixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        return pixels;
    }

    static void SaveWebp(BitmapSource source, string output, int quality)
    {
        var pixels = RentBgra(source, out var w, out var h);
        try { WebpWriter.Save(output, pixels, w, h, quality); }
        finally { ArrayPool<byte>.Shared.Return(pixels); }
    }

    static BitmapSource FlattenOnWhite(BitmapSource source, double dpiX, double dpiY)
    {
        var pixels = RentBgra(source, out var w, out var h);
        try
        {
            for (int i = 0, len = w * h * 4; i < len; i += 4)
            {
                int a = pixels[i + 3];
                if (a == 255) continue;
                int bg = 255 * (255 - a);
                pixels[i] = (byte)((pixels[i] * a + bg) / 255);
                pixels[i + 1] = (byte)((pixels[i + 1] * a + bg) / 255);
                pixels[i + 2] = (byte)((pixels[i + 2] * a + bg) / 255);
                pixels[i + 3] = 255;
            }
            return BitmapSource.Create(w, h, dpiX, dpiY, PixelFormats.Bgr32, null, pixels, w * 4);
        }
        finally { ArrayPool<byte>.Shared.Return(pixels); }
    }

    static void SaveJpeg(BitmapSource image, string output, int quality, BitmapMetadata? exif)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(image, null, exif, null));
        using var outStream = File.Create(output);
        encoder.Save(outStream);
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { }
    }
}
