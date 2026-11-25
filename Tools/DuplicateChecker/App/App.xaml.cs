using System.Windows;

namespace DuplicateChecker;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);
        MainWindow=new CheckerWindow();
        MainWindow.Show();
    }
}
