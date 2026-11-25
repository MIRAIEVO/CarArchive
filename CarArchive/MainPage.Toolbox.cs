using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CarArchive;

public sealed partial class MainPage
{
    private void InitializeToolbox()
    {
        ToolboxIcon.Content=NavigationIcon("toolbox",18,foregroundSource:ToolboxButton);
        DuplicateCheckerMenuItem.Icon=new SymbolIcon(Symbol.Copy);
    }

    private void OpenDuplicateChecker_Click(object sender,RoutedEventArgs args)
    {
        try
        {
            var executable=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","Tools","DuplicateChecker","DuplicateChecker.exe"));
            if(!File.Exists(executable)){VM.Status="找不到重复文件检查工具。请保留便携包中的 Tools 文件夹。";return;}
            using var launched=Process.Start(new ProcessStartInfo(executable){WorkingDirectory=Path.GetDirectoryName(executable)!,UseShellExecute=true});
        }
        catch(Exception error){VM.Status="无法打开重复文件检查 · "+error.Message;}
    }
}
