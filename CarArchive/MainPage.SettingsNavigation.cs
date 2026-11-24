using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
namespace CarArchive;
public sealed partial class MainPage
{
    private Grid BuildSettingsNavigation(params FrameworkElement[] pages)
    {
        var layout=new Grid { Width=Math.Min(480,Math.Max(300,XamlRoot.Size.Width-90)),Height=Math.Min(530,Math.Max(260,XamlRoot.Size.Height-230)),RowSpacing=14 };
        layout.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition());
        var header=new StackPanel {Orientation=Orientation.Horizontal,Spacing=10};
        var back=new Button {Content="← 返回",Visibility=Visibility.Collapsed};
        var title=new TextBlock {Text="选择设置项目",FontSize=18,VerticalAlignment=VerticalAlignment.Center};header.Children.Add(back);header.Children.Add(title);layout.Children.Add(header);
        var content=new ContentControl {HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};Grid.SetRow(content,1);layout.Children.Add(content);
        var home=new StackPanel {Spacing=12};
        string[] titles=["识别与费用","命名学习","数据与维护","快捷键","确认弹窗","外观"];
        string[] descriptions=["API、模型、联网方式及调用预算","本地指南、学习记录与导出","便携数据、缓存和崩溃日志","查看或修改快捷键","逐项设置下次不再显示","跟随系统、浅色或深色"];
        string[] paths=["M10,2 H14 V5 L17,7 L20,6 L22,10 L19,12 L20,16 L17,19 L14,18 V22 H10 V18 L7,19 L4,16 L5,12 L2,10 L4,6 L7,7 L10,5 Z M9,9 V15 H15 V9 Z","M3,3 H11 V7 H21 V21 H3 Z M6,10 V12 H18 V10 Z M6,15 V17 H15 V15 Z","M3,2 H21 V8 H3 Z M3,10 H21 V16 H3 Z M3,18 H21 V22 H3 Z","M2,5 H22 V20 H2 Z M5,8 V10 H8 V8 Z M10,8 V10 H13 V8 Z M15,8 V10 H19 V8 Z M6,14 V17 H18 V14 Z","M3,3 H21 V18 H14 L8,23 V18 H3 Z M7,8 V10 H17 V8 Z M7,12 V14 H14 V12 Z"];
        var views=pages.Select(page=>new ScrollViewer {Content=page,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}).ToArray();
        for(var i=0;i<pages.Length;i++)
        {
            var index=i;var row=new Grid {ColumnSpacing=14};row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(i<paths.Length?(IconElement)XamlReader.Load("<PathIcon xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\""+paths[i]+"\"/>"):
                new FontIcon {Glyph="\uE790"});
            var labels=new StackPanel {Spacing=4};labels.Children.Add(new TextBlock {Text=titles[i],FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});labels.Children.Add(new TextBlock {Text=descriptions[i],FontSize=12,TextWrapping=TextWrapping.Wrap});Grid.SetColumn(labels,1);row.Children.Add(labels);
            var button=new Button {Content=row,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(16),CornerRadius=new CornerRadius(10)};
            button.Click+=(_,_)=> {content.Content=views[index];UiMotion.Reveal(views[index]);title.Text=titles[index];back.Visibility=Visibility.Visible;};home.Children.Add(button);
        }
        var homeView=new ScrollViewer {Content=home,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        back.Click+=(_,_)=> {content.Content=homeView;UiMotion.Reveal(homeView);title.Text="选择设置项目";back.Visibility=Visibility.Collapsed;};content.Content=homeView;return layout;
    }
    private async void ClearRememberedNames_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            if(!await ConfirmAsync("clearNamingData","删除记住命名数据",Paragraph("清除已记住名称、用户学习样本、用词规则及记住名称日志。内置车型指南、照片、API 和目录设置将保留。"),"清除"))return;
            await VM.ClearNamingDataAsync();VM.Status="已清除记住命名数据。";
        }
        catch(Exception error){VM.Status=error.Message;}
    }
    private async void ClearCrashLogs_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            if(!await ConfirmAsync("clearCrashLogs","删除崩溃日志",Paragraph("删除本版本已保存的崩溃报告。当前会话继续记录，供出现新故障时诊断。"),"清除"))return;
            var count=CrashJournal.Current?.ClearReports()??0;VM.Status=$"已删除 {count} 份崩溃报告。";
        }
        catch(Exception error){VM.Status=error.Message;}
    }
}
