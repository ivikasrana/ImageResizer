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
        LoadTheme(IsDark);
    }

    public static void ApplyTheme(bool dark)
    {
        LoadTheme(dark);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, dark ? "dark" : "light");
        }
        catch { }
    }

    static void LoadTheme(bool dark)
    {
        IsDark = dark;
        Current.Resources.MergedDictionaries[0] =
            new ResourceDictionary { Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative) };
    }
}
