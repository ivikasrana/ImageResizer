using System.IO;
using System.Windows;

namespace ImageResizer;

public partial class App : Application
{
    static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageResizer", "theme.txt");

    public static bool IsDark { get; private set; } = true;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try { IsDark = !File.Exists(SettingsFile) || File.ReadAllText(SettingsFile).Trim() != "light"; } catch { }
        ApplyTheme(IsDark);
    }

    public static void ApplyTheme(bool dark)
    {
        IsDark = dark;
        var dicts = Current.Resources.MergedDictionaries;
        dicts[0] = new ResourceDictionary { Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative) };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, dark ? "dark" : "light");
        }
        catch { }
    }
}
