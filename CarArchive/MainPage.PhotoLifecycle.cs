using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CarArchive;
public sealed partial class MainPage
{
    private Task? independentPhotoPreparation;
    private bool independentPhotoPreparationRequested;
    private Task PrepareIndependentPhotosAsync()
    {
        independentPhotoPreparationRequested=true;
        if(independentPhotoPreparation is { IsCompleted:false }) return independentPhotoPreparation;
        return independentPhotoPreparation=PrepareAsync();
        async Task PrepareAsync()
        {
            while(independentPhotoPreparationRequested && !categoryClosed)
            {
                independentPhotoPreparationRequested=false;
                try { await VM.RecoverEditHistoryAsync();await VM.EnsureIndependentPhotoSourcesAsync(); }
                catch(OperationCanceledException) { VM.Status="独立照片准备已取消，可在操作记录中恢复。"; }
                catch(Exception error) { VM.Status="独立照片准备未完成："+error.Message;Notice.Severity=InfoBarSeverity.Warning; }
            }
            if(initialized && !categoryClosed) RenderSelection();
        }
    }
    private async Task PrepareAfterImportAsync(Task backgroundImport)
    {
        try { await backgroundImport;if(!categoryClosed) await PrepareIndependentPhotosAsync(); }
        catch(OperationCanceledException) { }
        catch(Exception error) { VM.Status="独立照片准备未完成："+error.Message; }
    }
    private void RecycleGroups_Click(object sender,RoutedEventArgs e)=>ExecuteCommand("recycleGroups",sender,e);
    private void RecyclePhotos_Click(object sender,RoutedEventArgs e)=>ExecuteCommand("recyclePhotos",sender,e);
    private static void ValidateRecycleGroup(PhotoGroup group,IReadOnlyList<Photo> photos,IReadOnlyList<ArchiveJob> jobs)
    {
        GroupPhotoEditor.Validate(group,photos);
        if(group.Archived) throw new InvalidOperationException("已归档照片不会被回收；可仅清理分组记录。");
        if(jobs.Any(j=>j.GroupId==group.Id && j.Status is "Pending" or "Interrupted")) throw new InvalidOperationException("请先在操作记录中处理本组的中断归档任务。");
    }
    private static TextBlock RecycleMessage(IEnumerable<Photo> photos,int groups)
    {
        var list=photos.DistinctBy(p=>p.Path,StringComparer.OrdinalIgnoreCase).ToArray();
        return Paragraph($"将从 {groups} 个分组中移除并回收 {list.Length} 张源照片：\n"+string.Join("\n",list.Take(8).Select(p=>p.Path))+(list.Length>8?$"\n…另有 {list.Length-8} 张":"")+"\n\n照片仅移入 Windows 回收站；无法回收时停止，不会永久删除。其他组的独立副本和已归档文件保持不变。");
    }
    private async Task RecycleGroupsCoreAsync()
    {
        try
        {
            FlushEditor();var groups=GroupList.SelectedItems.OfType<PhotoGroup>().Distinct().ToArray();if(groups.Length==0) return;
            var jobs=VM.Store.Jobs();foreach(var group in groups) ValidateRecycleGroup(group,group.Photos.ToArray(),jobs);
            if(!await ConfirmAsync("recycleGroups","删除分组并回收源照片",RecycleMessage(groups.SelectMany(g=>g.Photos),groups.Length),"移入回收站")) return;
            await VM.RecycleGroupsAsync(groups);RenderSelection();
        }
        catch(Exception error) { VM.Status=error.Message;Notice.Severity=InfoBarSeverity.Warning; }
    }
    private async Task RecyclePhotosCoreAsync()
    {
        try
        {
            FlushEditor();var group=RequireGroup();var selected=PhotoList.SelectedItems.OfType<Photo>().ToArray();ValidateRecycleGroup(group,selected,VM.Store.Jobs());
            if(!await ConfirmAsync("recyclePhotos","删除组内图片并回收源照片",RecycleMessage(selected,1),"移入回收站")) return;
            await VM.RecyclePhotosAsync(group,selected);RenderSelection();
        }
        catch(Exception error) { VM.Status=error.Message;Notice.Severity=InfoBarSeverity.Warning; }
    }
    private FrameworkElement BuildPhotoOperationHistoryPanel()
    {
        var panel=new StackPanel { Spacing=8 };var operations=VM.PhotoOperations.ToList();
        var list=new ListView { MaxHeight=170,MinWidth=340 };
        var details=Paragraph("选择独立副本或源照片回收记录。中断回收仅恢复已经计划且校验通过的文件，绝不永久删除。");
        var resume=new Button { Content="恢复所选照片操作" };var cancel=new Button { Content="取消所选副本任务" };var trash=new Button { Content="打开回收站" };trash.Click+=OpenRecycleBin_Click;
        void Reload(string? id=null)
        { operations=VM.PhotoOperations.ToList();list.ItemsSource=operations.Select(o=>$"{o.CreatedAt:MM-dd HH:mm} · {(o.Kind=="Recycle"?"回收源照片":o.Kind=="Isolate"?"升级独立副本":"复制分组")} · {o.Status} · {o.Entries.Count(e=>e.Stage is "Ready" or "Attached" or "Recycled" or "Removed")}/{o.Entries.Count} 张").ToArray();if(id is not null) list.SelectedIndex=operations.FindIndex(o=>o.Id==id); }
        void Show()
        {
            var op=list.SelectedIndex>=0 && list.SelectedIndex<operations.Count?operations[list.SelectedIndex]:null;
            resume.IsEnabled=op is not null && PhotoLifecycleService.Unfinished(op);
            cancel.IsEnabled=op is not null && op.Kind is "Copy" or "Isolate" && PhotoLifecycleService.Unfinished(op);
            if(op is not null) details.Text=string.Join("\n\n",op.Entries.Select(e=>$"{e.Stage}\n{e.Source}"+(e.Destination.Length>0?"\n→ "+e.Destination:"")+(e.Error.Length>0?"\n错误："+e.Error:"")));
        }
        Reload();list.SelectionChanged+=(_,_)=>Show();Show();
        async Task Act(bool abandon)
        {
            if(list.SelectedIndex<0 || list.SelectedIndex>=operations.Count) return;var operation=operations[list.SelectedIndex];resume.IsEnabled=cancel.IsEnabled=false;
            try { if(abandon) await VM.CancelPhotoCopyAsync(operation.Id);else await VM.ResumePhotoOperationAsync(operation.Id);Reload(operation.Id);Show(); }
            catch(Exception error) { details.Text=error.Message;Reload(operation.Id);Show(); }
        }
        resume.Click+=async(_,_)=>await Act(false);cancel.Click+=async(_,_)=>await Act(true);
        foreach(var child in new FrameworkElement[]{list,new ScrollViewer { Content=details,MaxHeight=190 },resume,cancel,trash}) panel.Children.Add(child);
        return panel;
    }
    private void OpenRecycleBin_Click(object sender,RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe","shell:RecycleBinFolder") { UseShellExecute=true }); }
        catch(Exception error) { VM.Status=error.Message; }
    }
}
