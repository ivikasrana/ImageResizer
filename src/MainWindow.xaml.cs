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

    // No title bar: drag the window by any empty area; right-click for Minimize / Close.
    void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
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
        BitmapFrame frame;
        using (var fs = File.OpenRead(input))
            frame = BitmapFrame.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

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
        if (rotation != 0) source = new TransformedBitmap(source, new RotateTransform(rotation));
        if (scale < 1.0) source = new TransformedBitmap(source, new ScaleTransform(scale, scale));

        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var pixels = new byte[w * h * 4];
        bgra.CopyPixels(pixels, w * 4, 0);

        if (webp)
        {
            WebpWriter.Save(output, pixels, w, h, quality);
            return;
        }

        // JPG has no alpha: flatten onto white.
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int a = pixels[i + 3];
            if (a == 255) continue;
            pixels[i] = (byte)((pixels[i] * a + 255 * (255 - a)) / 255);
            pixels[i + 1] = (byte)((pixels[i + 1] * a + 255 * (255 - a)) / 255);
            pixels[i + 2] = (byte)((pixels[i + 2] * a + 255 * (255 - a)) / 255);
            pixels[i + 3] = 255;
        }

        var result = BitmapSource.Create(w, h, frame.DpiX, frame.DpiY, PixelFormats.Bgr32, null, pixels, w * 4);
        result.Freeze();

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

    void SetBusy(bool busy)
    {
        BtnStart.IsEnabled = BtnSource.IsEnabled = BtnDest.IsEnabled = !busy;
        BtnCancel.IsEnabled = busy;
    }
}
