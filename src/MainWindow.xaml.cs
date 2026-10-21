using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Microsoft.Win32;

namespace ImageResizer;

public partial class MainWindow : Window
{
    const int DwmwaUseImmersiveDarkMode = 20;

    CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();
        TxtSource.Text = TxtDest.Text = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        SourceInitialized += (_, _) => UpdateThemeChrome();
        Closing += (_, _) => _cts?.Cancel();
        UpdateThemeChrome();
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    void BtnTheme_Click(object sender, RoutedEventArgs e)
    {
        App.ApplyTheme(!App.IsDark);
        UpdateThemeChrome();
    }

    void UpdateThemeChrome()
    {
        Sky.Visibility = App.IsDark ? Visibility.Visible : Visibility.Collapsed;
        BtnTheme.Content = App.IsDark ? "" : "";
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = App.IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
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
        Process.Start(new ProcessStartInfo("https://github.com/ivikasrana/ImageResizer") { UseShellExecute = true });

    void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsInteractive(e.OriginalSource as DependencyObject)) return;
        DragMove();
    }

    static bool IsInteractive(DependencyObject? d)
    {
        for (; d is not null; d = d is Visual or Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is ButtonBase or TextBoxBase or RangeBase or Thumb or ScrollBar) return true;
        }
        return false;
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void BtnCancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        var src = TxtSource.Text;
        if (!Directory.Exists(src)) { LblStatus.Text = "Source folder does not exist."; return; }

        var options = new ResizeOptions(
            SldSize.Value / 100.0,
            (int)SldQuality.Value,
            RbWebp.IsChecked == true ? OutputFormat.Webp : OutputFormat.Jpg,
            ChkExif.IsChecked == true);
        if (options.Format == OutputFormat.Webp && !WebpWriter.IsAvailable())
        {
            LblStatus.Text = "Could not load the built-in WebP encoder. Choose JPG.";
            return;
        }

        var dest = TxtDest.Text;
        var recursive = ChkSub.IsChecked == true;
        var outRoot = BatchPlan.NewOutputFolder(dest);

        SetBusy(true);
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        LblStatus.Text = "Scanning folder...";
        Progress.Value = 0;

        try
        {
            var tasks = await Task.Run(() => BatchPlan.Create(src, dest, outRoot, options.Extension, recursive), ct);
            if (tasks.Count == 0)
            {
                LblStatus.Text = "No images found in the source folder.";
                return;
            }

            int done = 0, failed = 0;
            long lastReport = 0;
            IProgress<string> progress = new Progress<string>(name =>
            {
                Progress.Value = (Volatile.Read(ref done) + Volatile.Read(ref failed)) * 100.0 / tasks.Count;
                LblStatus.Text = $"{Volatile.Read(ref done)}/{tasks.Count} processed, {Volatile.Read(ref failed)} failed.\n{name}";
            });

            await Task.Run(() => Parallel.ForEach(tasks,
                new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) },
                task =>
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(task.Target)!);
                        ImageProcessor.Resize(task.Source, task.Target, options);
                        Interlocked.Increment(ref done);
                    }
                    catch { Interlocked.Increment(ref failed); }
                    var now = Environment.TickCount64;
                    var prev = Interlocked.Read(ref lastReport);
                    if (now - prev >= 100 && Interlocked.CompareExchange(ref lastReport, now, prev) == prev)
                        progress.Report(task.Source);
                }), ct);

            Progress.Value = 100;
            LblStatus.Text = $"Done. {done} of {tasks.Count} images saved to {outRoot} ({failed} failed).";
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

    void SetBusy(bool busy)
    {
        BtnStart.IsEnabled = BtnSource.IsEnabled = BtnDest.IsEnabled = !busy;
        BtnCancel.IsEnabled = busy;
    }
}
