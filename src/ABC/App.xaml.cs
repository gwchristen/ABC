using System.Windows;
using ABC.Services;

namespace ABC;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeService.Instance.Initialize();
    }
}
