using System.IO;
using System.Windows;

namespace ABC.Services;

public class ThemeService
{
    private static ThemeService? _instance;
    public static ThemeService Instance => _instance ??= new ThemeService();

    private bool _isDarkTheme;

    public bool IsDarkTheme => _isDarkTheme;

    public event EventHandler? ThemeChanged;

    private ThemeService()
    {
        _isDarkTheme = LoadPreference();
    }

    public void Initialize()
    {
        if (_isDarkTheme)
            ApplyTheme(true);
    }

    public void ToggleTheme()
    {
        _isDarkTheme = !_isDarkTheme;
        ApplyTheme(_isDarkTheme);
        SavePreference(_isDarkTheme);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ApplyTheme(bool isDark)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;

        var existing = dicts.FirstOrDefault(d =>
            d.Source?.OriginalString?.Contains("Theme.xaml", StringComparison.OrdinalIgnoreCase) == true);

        string themePath = isDark
            ? "pack://application:,,,/ABC;component/Resources/DarkTheme.xaml"
            : "pack://application:,,,/ABC;component/Resources/LightTheme.xaml";

        var newTheme = new ResourceDictionary { Source = new Uri(themePath) };

        if (existing != null)
        {
            int index = dicts.IndexOf(existing);
            dicts[index] = newTheme;
        }
        else
        {
            dicts.Insert(0, newTheme);
        }
    }

    private static bool LoadPreference()
    {
        try
        {
            string path = GetSettingsPath();
            if (File.Exists(path))
                return File.ReadAllText(path).Trim().Equals("dark", StringComparison.OrdinalIgnoreCase);
        }
        catch { }
        return false;
    }

    private static void SavePreference(bool isDark)
    {
        try
        {
            File.WriteAllText(GetSettingsPath(), isDark ? "dark" : "light");
        }
        catch { }
    }

    private static string GetSettingsPath()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ABC");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "theme.txt");
    }
}
