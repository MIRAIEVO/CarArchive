using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace CarArchive;

public partial class App : Application
{
    private Window? window;
    public static MainWindow? MainWindow { get; private set; }
    private WorkspaceLease? workspaceLease;
    public App()
    {
        UnhandledException+=(_,e)=>CrashJournal.Current?.Capture("WinUI",e.Exception);
        AppDomain.CurrentDomain.UnhandledException+=(_,e)=> { if(e.ExceptionObject is Exception error) CrashJournal.Current?.Capture("AppDomain",error); };
        TaskScheduler.UnobservedTaskException+=(_,e)=>CrashJournal.Trace("Task.Unobserved."+e.Exception.GetType().Name);
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var data=PortableData.ResolveDirectory();
        // Test harnesses always provide a fresh explicit data directory and never show migration UI.
        if(Environment.GetEnvironmentVariable("CARARCHIVE_DATA_DIR") is { Length:>0 } || File.Exists(Path.Combine(data,"cararchive.db")) || File.Exists(Path.Combine(data,"workspace-id.txt")))
        {
            try { StartWorkspace(data); }catch(Exception error) { ShowStartup(error.Message); }
            return;
        }
        ShowStartup(null);
    }
    private void StartWorkspace(string data)
    {
        PortableData.EnsureWritable(data);workspaceLease=WorkspaceLease.Acquire(data);
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER",Path.Combine(data,"webview"));
        CrashJournal.Current=new(Path.Combine(data,"crashes"));
        CrashJournal.Trace("Window.Creating");var previous=window;MainWindow=new MainWindow();
        CrashJournal.Trace("Window.Activating");window=MainWindow;window.Activate();previous?.Close();CrashJournal.Trace("Window.Ready");
    }
    private void ShowStartup(string? error)
    {
        var startup=new Window { Title="CarArchive · 数据工作区" };
        var root=new Grid { Padding=new Thickness(28) };root.Children.Add(new TextBlock { Text="正在准备本版本的数据目录…",TextWrapping=TextWrapping.Wrap });startup.Content=root;window=startup;
        var once=false;root.Loaded+=async (_,_)=>
        {
            if(once) return;once=true;
            if(error is not null)
            {
                await new ContentDialog { XamlRoot=root.XamlRoot,Title="无法启动工作区",Content=error,CloseButtonText="关闭" }.ShowAsync();startup.Close();return;
            }
            var data=PortableData.ResolveDirectory();
            try
            {
                // Probe the package root without creating Data, keeping import atomic and non-overwriting.
                PortableData.EnsureWritable(Path.GetDirectoryName(data)!);
                var sources=new ComboBox { Header="导入来源（旧版必须先关闭）",HorizontalAlignment=HorizontalAlignment.Stretch,MinWidth=450,ItemsSource=PortableData.DiscoverSources(data) };
                if(sources.Items.Count>0) sources.SelectedIndex=0;
                var browse=new Button { Content="选择其他数据目录…" };
                var message=new TextBlock { Text="每个版本的数据独立保存。导入会复制数据库、缓存、命名指南和加密 Key；旧数据保持不变。\n当前版本："+data,TextWrapping=TextWrapping.Wrap };
                var panel=new StackPanel { Spacing=14 };panel.Children.Add(message);panel.Children.Add(sources);panel.Children.Add(browse);
                var dialog=new ContentDialog { XamlRoot=root.XamlRoot,Title="建立本版本工作区",Content=panel,PrimaryButtonText="导入所选数据",SecondaryButtonText="创建空工作区",CloseButtonText="退出",IsPrimaryButtonEnabled=sources.SelectedItem is not null };
                browse.Click+=async (_,_)=>
                {
                    var picker=new FolderPicker();picker.FileTypeFilter.Add("*");WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(startup));
                    var folder=await picker.PickSingleFolderAsync();if(folder is null) return;
                    var selected=new PortableDataSource(folder.Path,DateTime.Now);sources.ItemsSource=new[]{selected};sources.SelectedIndex=0;dialog.IsPrimaryButtonEnabled=true;
                };
                dialog.PrimaryButtonClick+=async (_,eventArgs)=>
                {
                    var deferral=eventArgs.GetDeferral();dialog.IsPrimaryButtonEnabled=false;dialog.IsSecondaryButtonEnabled=false;browse.IsEnabled=false;sources.IsEnabled=false;
                    try { if(sources.SelectedItem is not PortableDataSource selected) throw new InvalidOperationException("请选择旧版本 Data 或原 CarArchive 数据目录。");await PortableData.ImportAsync(selected.Path,data); }
                    catch(Exception e) { eventArgs.Cancel=true;message.Text=e.Message; }
                    finally { dialog.IsPrimaryButtonEnabled=true;dialog.IsSecondaryButtonEnabled=true;browse.IsEnabled=true;sources.IsEnabled=true;deferral.Complete(); }
                };
                var choice=await dialog.ShowAsync();if(choice==ContentDialogResult.None) { startup.Close();return; }
                StartWorkspace(data);
            }
            catch(Exception e) { await new ContentDialog { XamlRoot=root.XamlRoot,Title="数据初始化失败",Content=e.Message,CloseButtonText="关闭" }.ShowAsync();startup.Close(); }
        };
        startup.Activate();
    }
}
