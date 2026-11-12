using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.System;

namespace CarArchive;

public sealed partial class MainPage
{
    private async Task InitializeNamingGuideAsync()
    {
        try { await VM.PrepareNamingGuideAsync();await RefreshCategoryCatalogAsync(true); }
        catch(OperationCanceledException) { }
        catch(Exception error) { VM.Status="命名指南未完成，请打开本地命名指南重试："+error.Message; }
    }
    private async void NamingGuide_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            if(VM.State.Settings.ArchiveRoot.Length==0) throw new InvalidOperationException("请先选择归档根目录。");
            await VM.PrepareNamingGuideAsync();
            var details=new TextBlock { TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true };
            void Show() { var guide=VM.Naming.Guide;details.Text=$"来源：{guide?.SourceRoot}\n{guide?.Inventory.Count ?? 0} 个目录 · {guide?.Profiles.Count ?? 0} 条车型规则 · {guide?.Notes.Count ?? 0} 份补充文件\n\n默认：完整型号（去品牌，Body仅展示），括号按品牌/车系规则选择代次、代号或年代。奔驰、宝马、奥迪默认省略四驱标识；Porsche 911 中改前为底盘代号，中改后为代号+.2。指南完全本地运行，不发送给 Gemini。\n\n{VM.NamingGuideDirectory}\n\n重新扫描会备份并重建指南，已有照片和分类目录不变。"+(guide?.Warnings.Count>0?"\n\n"+string.Join("\n",guide.Warnings):""); }
            Show();var open=new Button { Content="打开详细命名指南表（CSV）" };var inventory=new Button { Content="打开全部目录扫描清单（CSV）" };var rebuild=new Button { Content="重新扫描并更新本地指南" };
            open.Click+=async(_,_)=> { try { await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(Path.Combine(VM.NamingGuideDirectory,"命名指南.csv"))); } catch(Exception error) { details.Text=error.Message; } };
            inventory.Click+=async(_,_)=> { try { await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(Path.Combine(VM.NamingGuideDirectory,"命名扫描清单.csv"))); } catch(Exception error) { details.Text=error.Message; } };
            rebuild.Click+=async(_,_)=> { rebuild.IsEnabled=false;try { await VM.EnsureNamingGuideAsync(true,true);await RefreshCategoryCatalogAsync(true);Show(); } catch(Exception error) { details.Text=error.Message; }finally { rebuild.IsEnabled=true; } };
            var panel=new StackPanel { Spacing=12,MaxWidth=550 };panel.Children.Add(details);panel.Children.Add(open);panel.Children.Add(inventory);panel.Children.Add(rebuild);
            await DialogAsync("本地车型命名指南",panel);
        }
        catch(Exception error) { VM.Status=error.Message; }
    }
}
