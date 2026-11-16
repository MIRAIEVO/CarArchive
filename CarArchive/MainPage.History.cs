using Microsoft.UI.Xaml;
using CarArchive.Core;
namespace CarArchive;
public sealed partial class MainPage
{
    private async void UndoGroup_Click(object sender,RoutedEventArgs e) { try { if(VM.Selected is { Archived:true } group) {var job=VM.Store.Jobs().Where(j=>j.GroupId==group.Id && j.Direction=="Archive" && j.Status=="Completed").OrderByDescending(j=>j.CreatedAt).First();await VM.RunGroupAsync(group,"撤销",true,ct=>VM.ResumeOrUndoAsync(job,true,ct));} else await VM.RestoreEditAsync(VM.Selected?.Id,false);RenderSelection(); }catch(System.Exception error) { VM.Status=error.Message; } }
    private async void RedoGroup_Click(object sender,RoutedEventArgs e) { try { if(VM.Selected is {} group && !VM.CanEditHistory(group.Id,true) && VM.Store.Jobs().Any(j=>j.GroupId==group.Id && j.Direction=="Archive" && j.Status=="Undone")) await RedoArchiveAsync(VM.Store.Jobs().Where(j=>j.GroupId==group.Id && j.Status=="Undone").OrderByDescending(j=>j.UndoneAt??j.CreatedAt).First());else await VM.RestoreEditAsync(VM.Selected?.Id,true);RenderSelection(); }catch(System.Exception error) { VM.Status=error.Message; } }
    private async System.Threading.Tasks.Task RedoArchiveAsync(ArchiveJob original)
    {
        var group=VM.Groups.First(g=>g.Id==original.GroupId);if(!group.Editable)throw new System.InvalidOperationException("本组当前不可重做归档。");
        var folderDirectory=System.IO.Path.GetDirectoryName(original.Entries[0].Destination)!;
        var brandDirectory=System.IO.Path.GetDirectoryName(folderDirectory)!;var root=System.IO.Path.GetDirectoryName(brandDirectory)!;
        var folder=System.IO.Path.GetFileName(folderDirectory);var brand=System.IO.Path.GetFileName(brandDirectory);
        if(!await ConfirmAsync("archive","重做原归档",Paragraph("按原路径归档到："+folderDirectory),"确认并移动","取消"))return;
        group.FolderName=folder;group.TargetBrand=brand;VM.SaveGroup(group);
        await VM.RunGroupAsync(group,"归档",true,ct=>VM.ArchiveAsync(group,ct,root,brand,folder));
    }
}
