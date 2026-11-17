using CarArchive.Core;
using CarArchive.Services;
using System.Text.Json;

namespace CarArchive.ViewModels;

public sealed partial class MainViewModel
{
    private PhotoLifecycleService? photoLifecycle;
    private PhotoLifecycleService PhotoLifecycle=>photoLifecycle ??= new(Database,Path.Combine(DataDirectory,"managedphotos"),new WindowsPhotoRecycler());
    public IReadOnlyList<PhotoFileOperation> PhotoOperations=>Database.PhotoOperations();
    public Task<PhotoGroup> DuplicateAsync(PhotoGroup source,CancellationToken ct=default)=>DuplicatePhotosAsync(source,source.Photos.ToList(),ct);
    public async Task<PhotoGroup> DuplicatePhotosAsync(PhotoGroup source,IReadOnlyList<Photo> photos,CancellationToken ct=default)
    {
        if(source.Archived || source.FilesPending || (source.IsWorking && source.TaskKind!="联网识别")) throw new InvalidOperationException("本组当前不可复制。");
        if(photos.Count==0 || photos.Any(p=>!source.Photos.Contains(p))) throw new InvalidOperationException("请选择本组中的照片。");
        var copy=new PhotoGroup { Photos=new(photos.Select(p=>p.CopyReference(true))),UserTouched=true,State="复制组" };copy.FilesPending=true;copy.State="正在准备独立照片副本";MarkTouched(source);
        Groups.Insert(Groups.IndexOf(source)+1,copy);Selected=copy;Save();StructureUpdated?.Invoke();PhotoFileOperation? operation=null;var applied=false;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,shutdown.Token);
        await Tasks.RunManyAsync(source.IsWorking?new[]{copy}:new[]{source,copy},"复制独立分组",true,async token=>
        {
            try
            {
                await FlushWritesAsync();operation=await Task.Run(()=>PhotoLifecycle.PlanCopyAsync(copy,source.Id,"Copy",token),token);
                var progress=ProgressFor(copy);await Task.Run(()=>PhotoLifecycle.CopyAsync(operation,progress,token),token);
                await AttachIndependentCopiesAsync(copy,operation,()=>applied=true);Status="已创建独立副本，新组可以单独框选和回收照片。";
            }
            catch
            {
                if(operation is not null && !applied) { try { await Task.Run(()=>PhotoLifecycle.AbandonCopyAsync(operation,default)); } catch { copy.FilesPending=true;SaveGroup(copy);throw; } }
                if(applied) { copy.FilesPending=true;copy.State="副本已生成，保存操作中断，请恢复照片操作";SaveGroup(copy); }
                if(!applied) { Groups.Remove(copy);if(Selected==copy) Selected=source;Save();StructureUpdated?.Invoke(); }
                throw;
            }
        },PhotoTaskChanged,linked.Token);
        if(!Groups.Contains(copy) || copy.FilesPending) throw new IOException(source.LastError.Length>0?source.LastError:"独立副本尚未就绪，请检查照片操作记录。");
        copy.TaskState=GroupTaskState.Idle;copy.TaskKind="";copy.State="待识别";SaveGroup(copy);GroupUpdated?.Invoke(copy);
        return copy;
    }
    private readonly SynchronizationContext? photoUi=SynchronizationContext.Current;
    private IProgress<string> ProgressFor(PhotoGroup group)=>new Progress<string>(text=> { void Update(){group.State=text;group.Refresh();if(Selected==group) Status=text;} if(photoUi is not null && SynchronizationContext.Current!=photoUi) photoUi.Post(_=>Update(),null);else Update(); });
    private void PhotoTaskChanged(PhotoGroup group)
    {
        if(!Groups.Contains(group)) return;SaveGroup(group);GroupUpdated?.Invoke(group);
    }
    private async Task AttachIndependentCopiesAsync(PhotoGroup group,PhotoFileOperation operation,Action? attached=null)
    {
        PhotoLifecycleService.AttachCopies(group,State,operation);attached?.Invoke();
        foreach(var entry in operation.Entries) if(State.Assets.FirstOrDefault(a=>a.Id==entry.NewAssetId) is { } asset) SaveAsset(asset);
        group.FilesPending=false;group.State="独立副本已就绪 · 待识别";SaveGroup(group);await FlushWritesAsync();
        await Task.Run(()=>PhotoLifecycle.CompleteCopy(operation));
    }
    public async Task EnsureIndependentPhotoSourcesAsync(CancellationToken ct=default)
    {
        var pending=Database.PhotoOperations().Where(PhotoLifecycleService.Unfinished).ToList();
        foreach(var operation in pending.Where(o=>o.Kind is "Copy" or "Isolate"))
        {
            if(Groups.FirstOrDefault(g=>g.Id==operation.GroupId) is not { } group) continue;
            group.FilesPending=true;SaveGroup(group);
            if(Store.Jobs().Any(j=>j.GroupId==group.Id && j.Status is "Pending" or "Interrupted")) { group.State="请先恢复已有归档操作，再准备独立副本";continue; }
            await ResumePhotoOperationAsync(operation.Id,ct);
        }
        foreach(var operation in pending.Where(o=>o.Kind=="Recycle"))
            if(Groups.FirstOrDefault(g=>g.Id==operation.GroupId) is { } group) { group.FilesPending=true;group.State="回收操作中断，请在操作记录检查";SaveGroup(group); }
        var owners=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var targets=new List<(PhotoGroup Group,List<Photo> Photos)>();
        foreach(var group in Groups.Where(g=>!g.Archived && !g.FilesPending && !g.IsWorking))
        {
            var shared=new List<Photo>();
            foreach(var set in group.Photos.GroupBy(p=>Path.GetFullPath(p.Path),StringComparer.OrdinalIgnoreCase))
            {
                if(owners.TryGetValue(set.Key,out var owner) && owner!=group.Id) shared.AddRange(set);
                else owners[set.Key]=group.Id;
            }
            if(shared.Count>0) targets.Add((group,shared));
        }
        if(targets.Count==0) return;
        await FlushWritesAsync();await Task.Run(Database.BackupPhotoLifecycleOnce,ct);
        foreach(var (group,photos) in targets)
        {
            if(Store.Jobs().Any(j=>j.GroupId==group.Id && j.Status is "Pending" or "Interrupted")) { group.FilesPending=true;group.State="共享照片准备受中断归档阻止，请先恢复归档";SaveGroup(group);continue; }
            var selectedIds=photos.Select(p=>p.Id).ToHashSet();group.FilesPending=true;SaveGroup(group);
            var sources=Groups.Where(g=>g.Id!=group.Id && g.Photos.Any(p=>photos.Any(selected=>Path.GetFullPath(selected.Path).Equals(Path.GetFullPath(p.Path),StringComparison.OrdinalIgnoreCase)))).ToList();
            if(sources.Any(g=>g.IsWorking || Store.Jobs().Any(j=>j.GroupId==g.Id && j.Status is "Pending" or "Interrupted"))) { group.State="共享原图所属组正在执行或有中断操作，请稍后准备副本";SaveGroup(group);continue; }
            var locked=sources.Append(group).ToList();
            await Tasks.RunManyAsync(locked,"升级独立照片副本",true,async token=>
            {
                var snapshot=new PhotoGroup { Id=group.Id,Photos=new(group.Photos.Where(p=>selectedIds.Contains(p.Id)).Select(p=>p.Snapshot())) };
                var operation=await Task.Run(()=>PhotoLifecycle.PlanCopyAsync(snapshot,"","Isolate",token),token);
                var progress=ProgressFor(group);await Task.Run(()=>PhotoLifecycle.CopyAsync(operation,progress,token),token);await AttachIndependentCopiesAsync(group,operation);
            },PhotoTaskChanged,ct);
        }
    }
    public async Task ResumePhotoOperationAsync(string operationId,CancellationToken ct=default)
    {
        var operation=Database.PhotoOperations().FirstOrDefault(o=>o.Id==operationId) ?? throw new InvalidOperationException("照片操作记录不存在。");
        var group=Groups.FirstOrDefault(g=>g.Id==operation.GroupId) ?? throw new InvalidOperationException("对应分组已不存在。");
        if(group.Archived) throw new InvalidOperationException("已归档组不能恢复源照片回收操作。");
        if(Store.Jobs().Any(j=>j.GroupId==group.Id && j.Status is "Pending" or "Interrupted")) throw new InvalidOperationException("请先完成该组已有归档操作。");
        group.FilesPending=true;
        await Tasks.RunAsync(group,"恢复照片操作",true,async token=>
        {
            if(operation.Kind is "Copy" or "Isolate") { var progress=ProgressFor(group);await Task.Run(()=>PhotoLifecycle.CopyAsync(operation,progress,token),token);await AttachIndependentCopiesAsync(group,operation); }
            else if(operation.Kind=="Recycle") { await ExecuteRecycleWithHistoryAsync(group,operation,token); }
            else throw new InvalidOperationException("无法识别的照片操作类型。");
        },()=>PhotoTaskChanged(group),ct);
    }
    public async Task CancelPhotoCopyAsync(string operationId,CancellationToken ct=default)
    {
        var operation=Database.PhotoOperations().FirstOrDefault(o=>o.Id==operationId) ?? throw new InvalidOperationException("照片操作记录不存在。");
        var group=Groups.FirstOrDefault(g=>g.Id==operation.GroupId);
        if(group?.IsWorking==true) throw new InvalidOperationException("请先取消正在执行的任务。");
        await Task.Run(()=>PhotoLifecycle.AbandonCopyAsync(operation,ct),ct);
        if(group is not null)
        {
            if(operation.Kind=="Copy") { Groups.Remove(group);if(Selected==group) Selected=Groups.FirstOrDefault();Save();StructureUpdated?.Invoke(); }
            else { group.FilesPending=false;group.State="已放弃副本准备 · 回收前仍需隔离共享原图";SaveGroup(group); }
        }
    }
    public Task RecycleGroupsAsync(IReadOnlyList<PhotoGroup> groups,CancellationToken ct=default)
        => RecycleSelectionAsync(groups.ToDictionary(g=>g,g=>(IReadOnlyList<Photo>)g.Photos.ToList()),ct);
    public Task RecyclePhotosAsync(PhotoGroup group,IReadOnlyList<Photo> photos,CancellationToken ct=default)
        => RecycleSelectionAsync(new Dictionary<PhotoGroup,IReadOnlyList<Photo>> { [group]=photos },ct);
    private async Task RecycleSelectionAsync(IReadOnlyDictionary<PhotoGroup,IReadOnlyList<Photo>> selection,CancellationToken ct)
    {
        if(selection.Count==0) throw new InvalidOperationException("请先选择待处理照片或分组。");
        foreach(var group in selection.Keys) RequireEditable(group);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,shutdown.Token);
        await Tasks.RunManyAsync(selection.Keys.ToList(),"删除并移入回收站",true,async token=>
        {
            foreach(var pair in selection)
            {
                var before=JsonSerializer.Serialize(pair.Key,StateStore.Json);
                var snapshot=new WorkspaceState { Groups=Groups.Select(g=>new PhotoGroup { Id=g.Id,Photos=new(g.Photos.Select(p=>p.Snapshot())) }).ToList() };
                await FlushWritesAsync();var operation=await Task.Run(()=>PhotoLifecycle.PlanRecycleAsync(pair.Key,pair.Value,snapshot,token),token);
                var record=new EditRecord {Recycle=true,PhotoOperationId=operation.Id,Kind="Recycle",Before=new(){[pair.Key.Id]=before},Positions=new(){[pair.Key.Id]=Groups.IndexOf(pair.Key)},Phase="Recycling"};
                foreach(var entry in operation.Entries)
                {
                    var backup=Path.Combine(DataDirectory,"undo",record.Id,entry.NewAssetId,Path.GetFileName(entry.Source));
                    await Task.Run(async()=>{Directory.CreateDirectory(Path.GetDirectoryName(backup)!);File.Copy(entry.Source,backup,false);if(await ArchiveService.HashAsync(backup,token)!=entry.Hash)throw new IOException("回收前备份校验失败，未删除源照片。");},token);
                    record.Backups[entry.Source]=backup;record.Hashes[entry.Source]=entry.Hash;
                }
                History.Add(record);SaveHistory();
                await ExecuteRecycleWithHistoryAsync(pair.Key,operation,token);
            }
        },PhotoTaskChanged,linked.Token);
    }
    private async Task ExecuteRecycleWithHistoryAsync(PhotoGroup group,PhotoFileOperation operation,CancellationToken ct)
    {
        try {await ExecuteRecycleAsync(group,operation,ct);}
        finally {
            await FlushWritesAsync();var record=History.LastOrDefault(r=>r.PhotoOperationId==operation.Id);
            if(record is not null) {record.After=Groups.Contains(group)?new(){[group.Id]=JsonSerializer.Serialize(group,StateStore.Json)}:[];record.Phase=operation.Status=="Completed"?"Ready":"Recycling";record.Sequence=DateTime.UtcNow.Ticks;SaveHistory();}
        }
    }
    private async Task ExecuteRecycleAsync(PhotoGroup group,PhotoFileOperation operation,CancellationToken ct)
    {
        // The coordinator owns the file-operation slot. Marshal only model changes back to the UI context.
        try { await PhotoLifecycle.RecycleAsync(operation,async entry=>
        {
            foreach(var photo in group.Photos.Where(p=>entry.PhotoIds.Contains(p.Id)).ToList()) group.Photos.Remove(photo);
            if(!group.Photos.Any(p=>p.Id==group.RepresentativeId)) group.RepresentativeId=group.Photos.FirstOrDefault()?.Id ?? "";
            if(!group.Photos.Any(p=>p.Id==group.LastViewedId)) group.LastViewedId=group.RepresentativeId;
            group.Suggestion=null;if(group.Verification is not null) group.VerificationHistory.Add(group.Verification);group.Verification=null;
            SaveGroup(group);await FlushWritesAsync();
        },ProgressFor(group),ct); }
        finally
        {
            group.FilesPending=PhotoLifecycleService.Unfinished(operation);
            group.State=group.FilesPending?"部分照片未完成回收，请检查照片操作记录":"源照片已移入回收站";
            SaveGroup(group);
        }
        if(group.Photos.Count==0 && operation.Status=="Completed")
        {
            var index=Groups.IndexOf(group);Groups.Remove(group);if(Selected==group) Selected=Groups.Skip(Math.Min(index,Groups.Count)).FirstOrDefault() ?? Groups.FirstOrDefault();Save();StructureUpdated?.Invoke();
        }
        else SaveGroup(group);
        Status=group.State;if(operation.Status!="Completed") throw new IOException("部分照片未完成回收；失败照片和恢复记录已保留。");
    }
}
