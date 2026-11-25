using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using System.Xml.Linq;
namespace CarArchive;
public sealed partial class MainPage
{
    private Grid BuildSettingsNavigation(params FrameworkElement[] pages)
    {
        var layout=new Grid { Width=Math.Min(492,Math.Max(230,XamlRoot.Size.Width-120)),Height=Math.Min(520,Math.Max(180,XamlRoot.Size.Height-230)),RowSpacing=12 };
        layout.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition());
        var header=new StackPanel {Orientation=Orientation.Horizontal,Spacing=10};
        var back=new Button {Content=new FontIcon{Glyph="\uE72B",FontSize=14},Visibility=Visibility.Collapsed,Padding=new Thickness(10,8,10,8),CornerRadius=new CornerRadius(8)};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(back,"返回设置首页");
        var title=new TextBlock {FontSize=16,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};header.Children.Add(back);header.Children.Add(title);layout.Children.Add(header);
        header.Visibility=Visibility.Collapsed;
        var content=new ContentControl {HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};Grid.SetRow(content,1);layout.Children.Add(content);
        var home=new StackPanel {Spacing=8,Padding=new Thickness(0,0,10,4)};
        string[] titles=["识别与费用","命名学习","数据与维护","快捷键","确认弹窗","外观"];
        string[] descriptions=["API、模型、联网方式及调用预算","本地指南、学习记录与导出","便携数据、缓存和崩溃日志","查看或修改快捷键","逐项设置下次不再显示","跟随系统、浅色或深色"];
        string[] icons=["recognition","naming","maintenance","shortcuts","confirmations","appearance"];
        var views=pages.Select(page=>new ScrollViewer {Content=page,Padding=new Thickness(0,0,10,6),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}).ToArray();
        for(var i=0;i<pages.Length;i++)
        {
            var index=i;var row=new Grid {ColumnSpacing=12};
            row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var icon=NavigationIcon(icons[i],21,(Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]);
            row.Children.Add(new Border{Width=38,Height=38,CornerRadius=new CornerRadius(10),Child=icon,
                Background=(Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"]});
            var labels=new StackPanel {Spacing=2,VerticalAlignment=VerticalAlignment.Center};
            labels.Children.Add(new TextBlock {Text=titles[i],FontSize=14,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
            labels.Children.Add(new TextBlock {Text=descriptions[i],FontSize=12,TextWrapping=TextWrapping.Wrap,Opacity=.75});
            Grid.SetColumn(labels,1);row.Children.Add(labels);
            var arrow=new FontIcon{Glyph="\uE76C",FontSize=12,Opacity=.7};Grid.SetColumn(arrow,2);row.Children.Add(arrow);
            var button=new Button {Content=row,MinHeight=58,HorizontalAlignment=HorizontalAlignment.Stretch,
                HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(12,8,12,8),CornerRadius=new CornerRadius(12)};
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,titles[i]);
            button.Click+=(_,_)=> {content.Content=views[index];title.Text=titles[index];header.Visibility=Visibility.Visible;back.Visibility=Visibility.Visible;UiMotion.Reveal(views[index]);back.Focus(FocusState.Programmatic);};
            home.Children.Add(button);
        }
        var homeView=new ScrollViewer {Content=home,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        back.Click+=(_,_)=> {content.Content=homeView;header.Visibility=Visibility.Collapsed;UiMotion.Reveal(homeView);};content.Content=homeView;return layout;
    }
    private static Viewbox NavigationIcon(string name,int size,Brush? fill=null,Control? foregroundSource=null)
    {
        using var stream=typeof(MainPage).Assembly.GetManifestResourceStream($"CarArchive.Assets.Settings.{name}.svg");
        var svg=XDocument.Load(stream!).Root!;
        var data=(string?)svg.Elements().Single().Attribute("d");
        var path=(Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load($"<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"F0 {data}\"/>");
        path.Fill=fill;
        if(foregroundSource is not null)
            path.SetBinding(Microsoft.UI.Xaml.Shapes.Shape.FillProperty,new Microsoft.UI.Xaml.Data.Binding{Source=foregroundSource,Path=new PropertyPath("Foreground")});
        var canvas=new Canvas{Width=24,Height=24};
        canvas.Children.Add(path);
        return new Viewbox{Width=size,Height=size,Child=canvas,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
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
