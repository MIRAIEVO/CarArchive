using System.Runtime.InteropServices;
using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace CarArchive;
public sealed partial class MainPage
{
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    private static int CurrentModifiers()=>((GetKeyState(17)&0x8000)!=0?1:0)|((GetKeyState(18)&0x8000)!=0?2:0)|((GetKeyState(16)&0x8000)!=0?4:0);
    private static bool DescendantOf(DependencyObject? node,DependencyObject ancestor)
    { for(;node is not null;node=VisualTreeHelper.GetParent(node)) if(ReferenceEquals(node,ancestor)) return true;return false; }
    private bool ModalOpen()=>pasteIdentityOpen || XamlRoot is not null && VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Any(p=>p.Child is { } child && VisualChild<ContentDialog>(child) is not null);
    private bool TextEditing()=>FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && (focused is TextBox or PasswordBox or RichEditBox || AncestorInput(focused));
    private static bool AncestorInput(DependencyObject node)
    { for(var p=VisualTreeHelper.GetParent(node);p is not null;p=VisualTreeHelper.GetParent(p)) if(p is TextBox or PasswordBox or RichEditBox) return true;return false; }
    private string CommandScope()=>FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focus?DescendantOf(focus,GroupList)?"Groups":DescendantOf(focus,PhotoList)?"Photos":"Global":"Global";
    private bool CanCommand(string id)
    {
        if(!initialized || ModalOpen()) return false;
        return id switch {
            "recognize"=>VM.SelectedCanRecognize,"verify"=>VM.Selected is { Photos.Count:>0 },
            "archive"=>VM.SelectedCanArchiveAction && (VM.Selected?.Archived==true || VM.State.Settings.ArchiveRoot.Length>0),
            "redoEdit"=>VM.CanEditHistory(null,true) || VM.Store.Jobs().Any(j=>j.Status=="Undone" && VM.Groups.Any(g=>g.Id==j.GroupId)),
            "previous" or "next"=>VM.Groups.Count>0,
            "removeGroups"=>!VM.Busy && GroupList.SelectedItems.OfType<PhotoGroup>().Any() && GroupList.SelectedItems.OfType<PhotoGroup>().All(g=>!g.IsWorking),
            "removePhotos"=>VM.SelectedEditable && PhotoList.SelectedItems.Count>0,
            "recycleGroups"=>!VM.Busy && GroupList.SelectedItems.OfType<PhotoGroup>().Any() && GroupList.SelectedItems.OfType<PhotoGroup>().All(g=>g.Editable),
            "recyclePhotos"=>VM.SelectedEditable && PhotoList.SelectedItems.Count>0,
            "undoArchive"=>VM.CanEditHistory(null,false) || VM.Store.Jobs().Any(j=>j.Direction=="Archive" && j.Status=="Completed"),_=>false };
    }
    private void ExecuteCommand(string id,object sender,RoutedEventArgs e)
    {
        if(!CanCommand(id)) return;
        if(id=="archive" && suggestionOwner is { } owner && suggestionsList?.SelectedItem is CategorySuggestion selected) AcceptSuggestion(owner,selected);
        if(id is not ("previous" or "next")) CloseAllSuggestions();
        switch(id) {
            case "recognize":if(VM.Selected is {IsWorking:true,TaskKind:"联网识别"}) Cancel_Click(sender,e);else QueryGroupAsync(false).Forget(VM);break;
            case "verify":CopyVerificationAsync().Forget(VM);break;
            case "redoEdit":GlobalRedoAsync().Forget(VM);break;
            case "previous":LocalAction(()=>VM.Navigate(-1),false);break;case "next":LocalAction(()=>VM.Navigate(1),false);break;
            case "archive":ArchiveCoreAsync().Forget(VM);break;case "undoArchive":GlobalUndoAsync().Forget(VM);break;
            case "removeGroups":RemoveGroupsCoreAsync().Forget(VM);break;case "removePhotos":RemovePhotosCoreAsync().Forget(VM);break;
            case "recycleGroups":RecycleGroupsCoreAsync().Forget(VM);break;case "recyclePhotos":RecyclePhotosCoreAsync().Forget(VM);break;
        }
    }
    private async Task GlobalRedoAsync() {var job=VM.Store.Jobs().Where(j=>j.Direction=="Archive" && j.Status=="Undone" && VM.Groups.Any(g=>g.Id==j.GroupId)).OrderByDescending(j=>j.UndoneAt??j.CreatedAt).FirstOrDefault();if(VM.LatestRedoTicks>(job?.UndoneAt?.UtcTicks??job?.CreatedAt.UtcTicks??0)){await VM.RestoreEditAsync(null,true);RenderSelection();}else if(job is not null)await RedoArchiveAsync(job);}
    private async Task GlobalUndoAsync() { var latestArchive=VM.Store.Jobs().Where(j=>j.Direction=="Archive" && j.Status=="Completed" && VM.Groups.Any(g=>g.Id==j.GroupId)).OrderByDescending(j=>j.CreatedAt).FirstOrDefault();if(VM.LatestEditTicks> (latestArchive?.CreatedAt.UtcTicks??0)) { await VM.RestoreEditAsync(null,false);RenderSelection(); } else await UndoCoreAsync(); }
    private void Commands_KeyDown(object sender,KeyRoutedEventArgs e)
    {
        if(e.Handled) return;
        var command=InteractionSettings.Match(VM.State.Settings,(int)e.Key,CurrentModifiers(),CommandScope(),TextEditing(),suggestionOwner is not null,e.KeyStatus.WasKeyDown,ModalOpen());
        if(command is null || !CanCommand(command.Id)) return;
        e.Handled=true;ExecuteCommand(command.Id,sender,e);
    }
    private void RefreshCommandHints()
    {
        foreach(var button in AnalysisBar.PrimaryCommands.OfType<AppBarButton>())
        { var id=ReferenceEquals(button,RecognizeActionButton)?"recognize":button.Label=="复制框选图片"?"verify":null;if(id is not null) ToolTipService.SetToolTip(button,button.Label+" · "+InteractionSettings.Format(InteractionSettings.Get(VM.State.Settings,id))); }
        ToolTipService.SetToolTip(ArchiveActionButton,"确认命名并移动 · "+InteractionSettings.Format(InteractionSettings.Get(VM.State.Settings,"archive")));
        DeleteGroupsMenu.KeyboardAcceleratorTextOverride=InteractionSettings.Format(InteractionSettings.Get(VM.State.Settings,"removeGroups"));RemovePhotosMenu.KeyboardAcceleratorTextOverride=InteractionSettings.Format(InteractionSettings.Get(VM.State.Settings,"removePhotos"));
        RecycleGroupsMenu.KeyboardAcceleratorTextOverride=InteractionSettings.Format(InteractionSettings.Get(VM.State.Settings,"recycleGroups"));RecyclePhotosMenu.KeyboardAcceleratorTextOverride=InteractionSettings.Format(InteractionSettings.Get(VM.State.Settings,"recyclePhotos"));
    }
    private FrameworkElement BuildInteractionSettingsPanel()
    {
        var panel=new StackPanel { Spacing=8 };panel.Children.Add(Paragraph("点击快捷键框后按组合键。输入框保留编辑操作；删除键仅作用于当前聚焦列表。更改即时保存。"));
        var status=Paragraph("");var edits=new Dictionary<string,TextBox>();
        foreach(var command in InteractionSettings.Commands)
        {
            var row=new Grid { ColumnSpacing=6 };row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=new GridLength(160)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            row.Children.Add(new TextBlock { Text=command.Label+(command.Scope=="Global"?"":" · "+(command.Scope=="Groups"?"分组列表":"组内图片")),TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center });
            var edit=new TextBox { Text=InteractionSettings.Format(InteractionSettings.Get(VM.State.Settings,command.Id)),IsReadOnly=true,PlaceholderText="按组合键" };Grid.SetColumn(edit,1);row.Children.Add(edit);edits[command.Id]=edit;
            edit.PreviewKeyDown+=(_,args)=> {
                if(args.Key is VirtualKey.Tab or VirtualKey.Escape) return;
                args.Handled=true;var key=(int)args.Key;if(key is 16 or 17 or 18 or >=160 and <=165) return;
                var proposed=new ShortcutBinding(key,CurrentModifiers());var error=InteractionSettings.Validate(command.Id,proposed,VM.State.Settings.Shortcuts);
                if(error is not null) { status.Text=error;return; }VM.State.Settings.Shortcuts[command.Id]=proposed;edit.Text=InteractionSettings.Format(proposed);VM.SaveHeader();RefreshCommandHints();status.Text="快捷键已保存。";
            };
            var clear=new Button { Content="清除" };Grid.SetColumn(clear,2);row.Children.Add(clear);clear.Click+=(_,_)=> { VM.State.Settings.Shortcuts[command.Id]=new(0,0);edit.Text="未设置";VM.SaveHeader();RefreshCommandHints(); };panel.Children.Add(row);
        }
        var resetKeys=new Button { Content="恢复默认快捷键" };resetKeys.Click+=(_,_)=> { VM.State.Settings.Shortcuts=InteractionSettings.Defaults();foreach(var pair in edits) pair.Value.Text=InteractionSettings.Format(InteractionSettings.Get(VM.State.Settings,pair.Key));VM.SaveHeader();RefreshCommandHints();status.Text="已恢复默认快捷键。"; };panel.Children.Add(resetKeys);panel.Children.Add(status);
        return panel;
    }
    private FrameworkElement BuildConfirmationSettingsPanel()
    {
        var confirms=new StackPanel { Spacing=6 };var toggles=new Dictionary<string,CheckBox>();
        foreach(var choice in InteractionSettings.Confirmations)
        {
            var toggle=new CheckBox { Content="不再显示 · "+choice.Value,IsChecked=VM.State.Settings.SkippedConfirmations.Contains(choice.Key) || choice.Key=="learnRule" && VM.State.Settings.Learning==LearningMode.Automatic };toggles[choice.Key]=toggle;
            void SaveConfirmation(object sender,RoutedEventArgs args) { if(toggle.IsChecked==true) VM.State.Settings.SkippedConfirmations.Add(choice.Key);else VM.State.Settings.SkippedConfirmations.Remove(choice.Key);if(choice.Key=="learnRule") VM.State.Settings.Learning=toggle.IsChecked==true?LearningMode.Automatic:LearningMode.ConfirmRules;VM.SaveHeader(); }
            toggle.Checked+=SaveConfirmation;toggle.Unchecked+=SaveConfirmation;confirms.Children.Add(toggle);
        }
        var resetPrompts=new Button { Content="恢复全部确认提示" };resetPrompts.Click+=(_,_)=> { VM.State.Settings.SkippedConfirmations.Clear();foreach(var toggle in toggles.Values) toggle.IsChecked=false;if(VM.State.Settings.Learning==LearningMode.Automatic) VM.State.Settings.Learning=LearningMode.ConfirmRules;VM.SaveHeader(); };confirms.Children.Add(resetPrompts);
        return confirms;
    }
}
internal static class UiTaskExtensions
{
    internal static async void Forget(this Task task,ViewModels.MainViewModel vm) { try { await task; } catch(Exception e) { vm.Status=e.Message; } }
}
