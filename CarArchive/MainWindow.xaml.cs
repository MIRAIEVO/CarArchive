using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CarArchive;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    internal static string AppIconPath => Path.Combine(AppContext.BaseDirectory,"Assets","AppIcon.ico");
    internal static string TitleBarIconPath => Path.Combine(AppContext.BaseDirectory,"Assets","Square44x44Logo.scale-200.png");
    internal bool HasTitleBarIcon => AppTitleBar.IconSource is ImageIconSource { ImageSource:BitmapImage image } && image.UriSource is { IsAbsoluteUri:true,IsFile:true } uri && File.Exists(uri.LocalPath);
    private bool sized;
    public void FitToDisplay(double scale)
    {
        if(sized) return; sized=true;
        var area=Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id,Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        var width=Math.Min((int)(1440*scale),Math.Max(640,area.Width-32));
        var height=Math.Min((int)(900*scale),Math.Max(480,area.Height-32));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X+(area.Width-width)/2,area.Y+(area.Height-height)/2,width,height));
    }
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon(AppIconPath);
        AppTitleBar.IconSource = new ImageIconSource { ImageSource=new BitmapImage(new Uri(TitleBarIconPath,UriKind.Absolute)) };
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1540, 980));

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }
}
