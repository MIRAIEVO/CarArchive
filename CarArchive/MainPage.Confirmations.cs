using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CarArchive;
public sealed partial class MainPage
{
    private async Task<bool> ConfirmAsync(string action,string title,object content,string primary="确认",string close="取消")
    {
        if(VM.State.Settings.SkippedConfirmations.Contains(action)) return true;
        CloseAllSuggestions();
        var remember=new CheckBox { Content=action=="learnRule"?"以后自动采用符合门槛的规则（切换到自动学习）":"下次不再显示，可在设置中恢复",Margin=new Thickness(0,12,0,0) };
        var panel=new StackPanel { Spacing=8 };panel.Children.Add(content as UIElement ?? Paragraph(content.ToString()??""));panel.Children.Add(remember);
        var result=await DialogAsync(title,panel,primary,null,close);
        if(result!=ContentDialogResult.Primary) return false;
        if(remember.IsChecked==true) { VM.State.Settings.SkippedConfirmations.Add(action);if(action=="learnRule") VM.State.Settings.Learning=LearningMode.Automatic;VM.SaveHeader(); }
        return true;
    }
}
