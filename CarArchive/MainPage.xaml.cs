using Microsoft.UI.Xaml.Controls;
using CarArchive.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CarArchive;

/// <summary>
/// The main content page displayed inside the application window.
/// Add your UI logic, event handlers, and data binding here.
/// </summary>
public sealed partial class MainPage : Page
{
    public MainViewModel VM { get; } = new();
    public MainPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => { FlushEditor(); VM.Save(); };
    }
}
