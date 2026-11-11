using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CarArchive;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    public static MainWindow? MainWindow { get; private set; }
    private System.Threading.Mutex? instanceMutex;
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
#if DEBUG
        AppDomain.CurrentDomain.ProcessExit+=(_,_)=> { try { System.IO.File.AppendAllText(System.IO.Path.Combine(ViewModels.MainViewModel.DataDirectory,"lifecycle.log"),$"ProcessExit {DateTimeOffset.Now:O} code={Environment.ExitCode}\n"); }catch { } };
#endif
        UnhandledException += (_, e) =>
        {
            try { System.IO.Directory.CreateDirectory(ViewModels.MainViewModel.DataDirectory); System.IO.File.AppendAllText(System.IO.Path.Combine(ViewModels.MainViewModel.DataDirectory,"crash.log"),$"{DateTimeOffset.Now:O} {e.Exception}\n"); } catch { }
        };
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ViewModels.MainViewModel.DataDirectory.ToUpperInvariant())));
        instanceMutex=new System.Threading.Mutex(true,"Local\\CarArchive-"+hash,out var firstInstance);
        if(!firstInstance) { Exit(); return; }
        MainWindow = new MainWindow();
#if DEBUG
        MainWindow.Closed+=(_,_)=> { try { System.IO.File.AppendAllText(System.IO.Path.Combine(ViewModels.MainViewModel.DataDirectory,"lifecycle.log"),$"WindowClosed {DateTimeOffset.Now:O}\n{Environment.StackTrace}\n"); }catch { } };
#endif
        _window = MainWindow;
        _window.Activate();
    }
}
