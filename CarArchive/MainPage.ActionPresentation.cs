using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CarArchive;
public sealed partial class MainPage
{
    private void InitializeActionPalette()=>ActualThemeChanged+=(_,_)=>RefreshActionPresentation();
    private void SetReversalPalette(Control button,bool enabled)
    {
        foreach(var key in new[]{"ButtonBackground","ButtonBackgroundPointerOver","ButtonBackgroundPressed","AppBarButtonBackgroundPointerOver","AppBarButtonBackgroundPressed"})
            if(enabled)button.Resources[key]=Resources[key.EndsWith("PointerOver",StringComparison.Ordinal)?"ReversalHoverBrush":key.EndsWith("Pressed",StringComparison.Ordinal)?"ReversalPressedBrush":"ReversalBackgroundBrush"];else button.Resources.Remove(key);
        foreach(var key in new[]{"ButtonBorderBrushPointerOver","ButtonBorderBrushPressed","AppBarButtonBorderBrushPointerOver","AppBarButtonBorderBrushPressed"})
            if(enabled)button.Resources[key]=Resources["ReversalBorderBrush"];else button.Resources.Remove(key);
    }
    private void RefreshActionPresentation()
    {
        if(!initialized) return;
        RestoreFolderNameButton.IsEnabled=VM.Selected is {Editable:true,PreviousFolderName.Length:>0};
        var cancel=VM.Selected is {IsWorking:true,TaskKind:"联网识别"};
        RecognizeActionButton.Label=cancel?"取消识别":"Gemini识别";
        RecognizeActionButton.Icon=new SymbolIcon(cancel?Symbol.Cancel:Symbol.Find);
        RecognizeActionButton.Style=(Style)Resources[cancel?"RecognitionCancelButtonStyle":"RecognitionButtonStyle"];
        SetReversalPalette(RecognizeActionButton,cancel);
        var undo=VM.Selected?.Archived==true;
        SetReversalPalette(ArchiveActionButton,undo);
        ArchiveActionButton.Content=undo?"撤回移动":"确认并移动";
        ArchiveActionButton.Style=(Style)Resources[undo?"UndoMoveActionStyle":"MoveActionStyle"];
        ToolTipService.SetToolTip(ArchiveActionButton,(undo?"撤回当前组的移动":"确认命名并移动")+" · "+InteractionSettings.Format(InteractionSettings.Get(VM.State.Settings,"archive")));
        RefreshCommandHints();
    }
    private async Task UndoSelectedMoveAsync(PhotoGroup group)
    {
        var job=VM.Store.Jobs().Where(job=>job.GroupId==group.Id && job.Direction=="Archive" && job.Status=="Completed").OrderByDescending(job=>job.CreatedAt).FirstOrDefault()
            ?? throw new InvalidOperationException("此组没有可撤回的移动记录。");
        await VM.RunGroupAsync(group,"撤销",true,ct=>VM.ResumeOrUndoAsync(job,true,ct));
        if(VM.Selected==group) RenderSelection();
    }
}
