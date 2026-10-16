using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ImageResizer;

public partial class MainWindow : Window
{
    static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

    CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();
        TxtSource.Text = TxtDest.Text = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        SourceInitialized += (_, _) => UpdateThemeChrome();
        UpdateThemeChrome();
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    void BtnTheme_Click(object sender, RoutedEventArgs e)
    {
        App.ApplyTheme(!App.IsDark);
        UpdateThemeChrome();
    }

    // Icon on the toggle button (shows what you'll switch to) and dark/light native title bar.
    void UpdateThemeChrome()
    {
        Sky.Visibility = App.IsDark ? Visibility.Visible : Visibility.Collapsed;
        BtnTheme.Content = App.IsDark ? "" : "";
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = App.IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
    }

    void BtnSource_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { InitialDirectory = TxtSource.Text };
        if (dlg.ShowDialog() != true) return;
        if (TxtDest.Text == TxtSource.Text) TxtDest.Text = dlg.FolderName;
        TxtSource.Text = dlg.FolderName;
    }

    void BtnDest_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { InitialDirectory = TxtDest.Text };
        if (dlg.ShowDialog() == true) TxtDest.Text = dlg.FolderName;
    }

    void BtnGitHub_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/ivikasrana/ImageResizer") { UseShellExecute = true });

    // No title bar: drag the window from anywhere that isn't an interactive control.
    void Window_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                 ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.Primitives.RangeBase or System.Windows.Controls.Primitives.Thumb
                or System.Windows.Controls.Primitives.ScrollBar) return;
        }
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void BtnCancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        var src = TxtSource.Text;
        if (!Directory.Exists(src)) { LblStatus.Text = "Source folder does not exist."; return; }

        var toWebp = RbWebp.IsChecked == true;
        if (toWebp && !WebpWriter.IsAvailable())
        {
            LblStatus.Text = "Could not load the built-in WebP encoder. Choose JPG.";
            return;
        }

        var scale = SldSize.Value / 100.0;
        var quality = (int)SldQuality.Value;
        var keepExif = ChkExif.IsChecked == true;
        var option = ChkSub.IsChecked == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var outRoot = Path.Combine(TxtDest.Text, $"New_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}");

        SetBusy(true);
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        LblStatus.Text = "Scanning folder...";
        Progress.Value = 0;

        try
        {
            var files = await Task.Run(() => Directory.EnumerateFiles(src, "*", option)
                .Where(f => !f.StartsWith(outRoot, StringComparison.OrdinalIgnoreCase)
                            && Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .ToList(), ct);

            int done = 0, failed = 0;
            long lastReport = 0;
            var progress = new Progress<string>(name =>
            {
                Progress.Value = files.Count == 0 ? 0 : (done + failed) * 100.0 / files.Count;
                LblStatus.Text = $"{done}/{files.Count} processed, {failed} failed.\n{name}";
            });

            await Task.Run(() => Parallel.ForEach(files,
                new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) },
                file =>
                {
                    try
                    {
                        var rel = Path.GetRelativePath(src, file);
                        var target = Path.ChangeExtension(Path.Combine(outRoot, rel), toWebp ? ".webp" : ".jpg");
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        Resize(file, target, scale, quality, toWebp, keepExif);
                        Interlocked.Increment(ref done);
                    }
                    catch { Interlocked.Increment(ref failed); }
                    var now = Environment.TickCount64;
                    var prev = Interlocked.Read(ref lastReport);
                    if (now - prev >= 100 && Interlocked.CompareExchange(ref lastReport, now, prev) == prev)
                        ((IProgress<string>)progress).Report(file);
                }), ct);

            Progress.Value = 100;
            LblStatus.Text = $"Done. {done} of {files.Count} images saved to {outRoot} ({failed} failed).";
        }
        catch (OperationCanceledException) { LblStatus.Text = "Cancelled."; }
        catch (Exception ex) { LblStatus.Text = "Error: " + ex.Message; }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    static void Resize(string input, string output, double scale, int quality, bool webp, bool keepExif)
    {
        using var fs = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None).Frames[0];

        BitmapMetadata? metadata = null;
        double rotation = 0;
        try
        {
            metadata = frame.Metadata as BitmapMetadata;
            if (metadata?.GetQuery("/app1/ifd/{ushort=274}") is ushort o)
                rotation = o switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
        }
        catch { /* no EXIF */ }

        BitmapSource source = frame;
        if (scale < 1.0)
        {
            fs.Position = 0;
            var img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = fs;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.CreateOptions = BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreImageCache;
            img.DecodePixelWidth = Math.Max(1, (int)Math.Round(frame.PixelWidth * scale));
            img.DecodePixelHeight = Math.Max(1, (int)Math.Round(frame.PixelHeight * scale));
            img.EndInit();
            source = img;
        }
        if (rotation != 0) source = new TransformedBitmap(source, new RotateTransform(rotation));

        var hasAlpha = HasAlpha(frame);
        BitmapSource result;
        byte[]? pixels = null;
        try
        {
            if (webp || hasAlpha)
            {
                var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                int w = bgra.PixelWidth, h = bgra.PixelHeight, len = w * h * 4;
                pixels = ArrayPool<byte>.Shared.Rent(len);
                bgra.CopyPixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);

                if (webp)
                {
                    WebpWriter.Save(output, pixels, w, h, quality);
                    return;
                }

                // JPG has no alpha: flatten onto white.
                for (int i = 0; i < len; i += 4)
                {
                    int a = pixels[i + 3];
                    if (a == 255) continue;
                    int bg = 255 * (255 - a);
                    pixels[i] = (byte)((pixels[i] * a + bg) / 255);
                    pixels[i + 1] = (byte)((pixels[i + 1] * a + bg) / 255);
                    pixels[i + 2] = (byte)((pixels[i + 2] * a + bg) / 255);
                    pixels[i + 3] = 255;
                }
                result = BitmapSource.Create(w, h, frame.DpiX, frame.DpiY, PixelFormats.Bgr32, null, pixels, w * 4);
            }
            else
            {
                result = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
            }
        }
        finally
        {
            if (pixels is not null) ArrayPool<byte>.Shared.Return(pixels);
        }

        BitmapMetadata? exif = null;
        if (keepExif && metadata is not null)
        {
            try
            {
                exif = metadata.Clone();
                if (rotation != 0) exif.SetQuery("/app1/ifd/{ushort=274}", (ushort)1);
            }
            catch { exif = null; }
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(result, null, exif, null));
        using var outStream = File.Create(output);
        encoder.Save(outStream);
    }

    static bool HasAlpha(BitmapSource s)
    {
        var f = s.Format;
        if (f == PixelFormats.Bgra32 || f == PixelFormats.Pbgra32 || f == PixelFormats.Rgba64 || f == PixelFormats.Prgba64
            || f == PixelFormats.Rgba128Float || f == PixelFormats.Prgba128Float) return true;
        return s.Palette?.Colors.Any(c => c.A < 255) == true;
    }

    void SetBusy(bool busy)
    {
        BtnStart.IsEnabled = BtnSource.IsEnabled = BtnDest.IsEnabled = !busy;
        BtnCancel.IsEnabled = busy;
    }
}
