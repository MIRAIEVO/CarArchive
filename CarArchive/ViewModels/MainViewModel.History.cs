using System.Text.Json;
using CarArchive.Core;
namespace CarArchive.ViewModels;
public sealed partial class MainViewModel
{
    public sealed class EditRecord
    {
        public string Id {get;set;}=Guid.NewGuid().ToString("N");
        public Dictionary<string,string> Before {get;set;}=[];
        public Dictionary<string,string> After {get;set;}=[];
        public Dictionary<string,int> Positions {get;set;}=[];
        public Dictionary<string,string> Backups {get;set;}=[];
        public Dictionary<string,string> Hashes {get;set;}=[];
        public bool Recycle {get;set;}
        public string PhotoOperationId {get;set;}="";
        public string Kind {get;set;}="Edit";
        public bool Undone {get;set;}
        public string Phase {get;set;}="Ready";
        public long Sequence {get;set;}=DateTime.UtcNow.Ticks;
    }
    private List<EditRecord>? editRecords;
    private string HistoryPath=>Path.Combine(DataDirectory,"edit-history.json");
    private List<EditRecord> History=>editRecords??=File.Exists(HistoryPath)?JsonSerializer.Deserialize<List<EditRecord>>(File.ReadAllText(HistoryPath),StateStore.Json)??[]:[];
    private void SaveHistory() { var temp=HistoryPath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(History,StateStore.Json));File.Move(temp,HistoryPath,true); }
    public Dictionary<string,string> CaptureEdits(IEnumerable<PhotoGroup>? groups=null)=>(groups??Groups).ToDictionary(g=>g.Id,g=>JsonSerializer.Serialize(g,StateStore.Json));
    public void RecordEdits(Dictionary<string,string> before,HashSet<string>? scope=null,bool coalesce=false)
    {
        var after=CaptureEdits(scope is null?null:Groups.Where(g=>scope.Contains(g.Id)));var changed=before.Keys.Union(after.Keys).Where(id=>before.GetValueOrDefault(id)!=after.GetValueOrDefault(id)).ToHashSet();
        // Never restore groups which were running during the edit.
        changed.RemoveWhere(id=>Groups.FirstOrDefault(g=>g.Id==id)?.IsWorking==true || scope is not null && !scope.Contains(id));
        if(changed.Count==0) return;
        foreach(var job in Store.Jobs().Where(j=>j.Status=="Undone" && changed.Contains(j.GroupId))) {job.Status="RedoDiscarded";Enqueue(()=>Database.SaveJob(job));}
        History.RemoveAll(r=>r.Undone && r.Before.Keys.Union(r.After.Keys).Any(changed.Contains));
        var record=new EditRecord { Before=before.Where(x=>changed.Contains(x.Key)).ToDictionary(),After=after.Where(x=>changed.Contains(x.Key)).ToDictionary(),Positions=Groups.Select((g,i)=>(g.Id,i)).ToDictionary(x=>x.Id,x=>x.i) };
        var previous=History.LastOrDefault();
        if(coalesce && previous is {Undone:false,Kind:"Fields"} && record.Sequence-previous.Sequence<TimeSpan.FromSeconds(2).Ticks && previous.After.Keys.Order().SequenceEqual(record.After.Keys.Order())) {previous.After=record.After;previous.Sequence=record.Sequence;}
        else {record.Kind=coalesce?"Fields":"Edit";History.Add(record);}SaveHistory();
    }
    public async Task RestoreEditAsync(string? groupId,bool redo,CancellationToken ct=default)
    {
        var record=FindEdit(groupId,redo)??throw new InvalidOperationException("没有可撤回或重做的编辑。");
        var ids=record.Before.Keys.Union(record.After.Keys).ToHashSet();
        var groups=Groups.Where(g=>ids.Contains(g.Id)).ToArray();
        if(groups.Any(g=>g.IsWorking || g.FilesPending)) throw new InvalidOperationException("关联分组正在执行任务。");
        // File changes are serialized with archive/copy/recycle. The restore rechecks group state.
        var targets=groups.Length>0?groups:new[]{new PhotoGroup()};
        await Tasks.RunManyAsync(targets,"撤回编辑",true,token=>RestoreEditCoreAsync(groupId,redo,token),_=>{},ct);
        if(targets.Any(g=>g.TaskState==GroupTaskState.Failed)) throw new IOException(targets.First(g=>g.TaskState==GroupTaskState.Failed).LastError);
    }
    private EditRecord? FindEdit(string? groupId,bool redo)=>History.Where(r=>r.Undone==redo && (groupId is null || r.Before.ContainsKey(groupId)||r.After.ContainsKey(groupId))).OrderByDescending(r=>r.Sequence).FirstOrDefault();
    public long LatestRedoTicks=>FindEdit(null,true)?.Sequence??0;
    public long LatestEditTicks=>FindEdit(null,false)?.Sequence??0;
    public bool CanEditHistory(string? groupId,bool redo)=>FindEdit(groupId,redo) is not null;
    private async Task RestoreEditCoreAsync(string? groupId,bool redo,CancellationToken ct=default)
    {
        var record=FindEdit(groupId,redo)??throw new InvalidOperationException("没有可撤回或重做的编辑。");
        var ids=record.Before.Keys.Union(record.After.Keys).ToHashSet();
        if(Groups.Any(g=>ids.Contains(g.Id) && (g.IsWorking && g.TaskKind!="撤回编辑"||g.Archived||g.FilesPending))) throw new InvalidOperationException("请先完成任务或撤销归档，再撤回编辑。");
        if(History.Any(r=>r!=record && r.Sequence>record.Sequence && !r.Undone && r.Before.Keys.Union(r.After.Keys).Any(ids.Contains))) throw new InvalidOperationException("请先撤回此组后续关联操作。");
        var target=redo?record.After:record.Before;
        var restored=target.Values.Select(s=>JsonSerializer.Deserialize<PhotoGroup>(s,StateStore.Json)!).ToList();
        record.Phase=redo?"Redoing":"Undoing";SaveHistory();
        // Only app-owned independent copies can be moved into undo storage.
        var managed=Path.GetFullPath(Path.Combine(DataDirectory,"managedphotos"))+Path.DirectorySeparatorChar;
        if(record.Recycle)
        {
            if(!redo)
            {
                foreach(var file in record.Backups)
                {
                    if(!record.Hashes.TryGetValue(file.Key,out var expected)||!File.Exists(file.Value)||await ArchiveService.HashAsync(file.Value,ct)!=expected) throw new IOException("源照片撤回备份缺失或发生变化。");
                    if(File.Exists(file.Key)&&await ArchiveService.HashAsync(file.Key,ct)!=expected) throw new IOException("原位置已有不同文件，未覆盖："+file.Key);
                }
                foreach(var file in record.Backups) if(!File.Exists(file.Key)) {Directory.CreateDirectory(Path.GetDirectoryName(file.Key)!);await Task.Run(()=>File.Copy(file.Value,file.Key,false),ct);}
            }
            else foreach(var g in Groups.Where(g=>ids.Contains(g.Id)).ToList())
            {
                var photos=g.Photos.Where(p=>record.Backups.ContainsKey(p.Path)).ToList();if(photos.Count==0)continue;
                var state=new WorkspaceState{Groups=Groups.ToList()};
                var operation=await Task.Run(()=>PhotoLifecycle.PlanRecycleAsync(g,photos,state,ct),ct);await ExecuteRecycleAsync(g,operation,ct);
            }
        }
        else if(!redo)
        {
            var remaining=Groups.Where(g=>!ids.Contains(g.Id)).SelectMany(g=>g.Photos).Concat(restored.SelectMany(g=>g.Photos)).Select(p=>p.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var files=Groups.Where(g=>ids.Contains(g.Id)).SelectMany(g=>g.Photos).Select(p=>p.Path).Distinct(StringComparer.OrdinalIgnoreCase).Where(path=>path.StartsWith(managed,StringComparison.OrdinalIgnoreCase)&&!remaining.Contains(path)&&File.Exists(path));
            foreach(var path in files)
            {
                ct.ThrowIfCancellationRequested();var backup=Path.Combine(DataDirectory,"undo",record.Id,Guid.NewGuid().ToString("N"),Path.GetFileName(path));
                record.Backups[path]=backup;record.Hashes[path]=await Task.Run(()=>ArchiveService.HashAsync(path,ct),ct);SaveHistory();Directory.CreateDirectory(Path.GetDirectoryName(backup)!);File.Move(path,backup);
            }
        }
        else foreach(var file in record.Backups)
        {
            if(!File.Exists(file.Value)) { if(File.Exists(file.Key) && record.Hashes.TryGetValue(file.Key,out var hash) && await ArchiveService.HashAsync(file.Key,ct)==hash) continue;throw new IOException("撤回备份缺失或恢复位置有冲突。"); }
            if(record.Hashes.TryGetValue(file.Key,out var expectedHash) && await ArchiveService.HashAsync(file.Value,ct)!=expectedHash) throw new IOException("撤回备份已变化，已停止重做。");
            if(File.Exists(file.Key)) throw new IOException("重做位置存在文件，已停止，未覆盖："+file.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(file.Key)!);File.Move(file.Value,file.Key);
        }
        foreach(var g in Groups.Where(g=>ids.Contains(g.Id)).ToList()) Groups.Remove(g);
        foreach(var g in restored) {if(g.IsWorking){g.TaskState=GroupTaskState.Idle;g.TaskKind="";}}
        foreach(var g in restored.OrderBy(g=>record.Positions.GetValueOrDefault(g.Id,int.MaxValue)))
        {
            foreach(var photo in g.Photos) if(State.Assets.FirstOrDefault(a=>a.Id==photo.AssetId) is {} asset) photo.Attach(asset);
            Groups.Insert(Math.Min(record.Positions.GetValueOrDefault(g.Id,Groups.Count),Groups.Count),g);
        }
        Selected=Groups.FirstOrDefault(g=>g.Id==groupId)??restored.FirstOrDefault()??Groups.FirstOrDefault();
        Save();await FlushWritesAsync();record.Undone=!redo;record.Phase="Ready";record.Sequence=DateTime.UtcNow.Ticks;SaveHistory();StructureUpdated?.Invoke();
    }
    public async Task RecoverEditHistoryAsync()
    {
        foreach(var record in History.Where(r=>r.Phase is "Undoing" or "Redoing").OrderBy(r=>r.Sequence).ToList())
        {
            var redo=record.Phase=="Redoing";
            // Keep the same partially executed record at the top for idempotent resumption.
            record.Undone=redo;record.Sequence=DateTime.UtcNow.Ticks;SaveHistory();
            await RestoreEditAsync(record.Before.Keys.Union(record.After.Keys).FirstOrDefault(),redo);
        }
    }
    private async Task ClearEmptyWorkspaceCachesAsync(bool purgeUndo)
    {
        try {
            await BackgroundImport;await Images.DrainAsync();if(Groups.Count!=0 || Database.PhotoOperations().Any(PhotoLifecycleService.Unfinished) || Store.Jobs().Any(j=>j.Status is "Pending" or "Interrupted"))return;
            foreach(var name in purgeUndo?new[]{"cache","clipboard","undo","managedphotos"}:new[]{"cache","clipboard"}) {var directory=Path.Combine(DataDirectory,name);if(Directory.Exists(directory))Directory.Delete(directory,true);}
            if(purgeUndo){History.Clear();SaveHistory();State.Assets.Clear();}Save();Images.ClearCompletedRequests();
        } catch(Exception error) {Status="缓存清理未完成："+error.Message;}
    }
    private void PurgeArchivedHistory(IEnumerable<PhotoGroup> removed)
    {
        var ids=removed.Where(g=>g.Archived).Select(g=>g.Id).ToHashSet();
        foreach(var record in History.Where(r=>r.Before.Keys.Union(r.After.Keys).Any(ids.Contains)).ToList())
        {
            foreach(var file in record.Backups.Values) if(File.Exists(file)) File.Delete(file);
            History.Remove(record);
        }
        var retained=Groups.SelectMany(g=>g.Photos).Select(p=>p.ThumbnailPath).Concat(Groups.SelectMany(g=>g.Photos).Select(p=>p.PreviewPath)).Concat(Groups.SelectMany(g=>g.Photos).Select(p=>p.AnalysisPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cacheRoot=Path.GetFullPath(Path.Combine(DataDirectory,"cache"))+Path.DirectorySeparatorChar;
        foreach(var photo in removed.Where(g=>g.Archived).SelectMany(g=>g.Photos)) foreach(var path in new[]{photo.ThumbnailPath,photo.PreviewPath,photo.AnalysisPath})
            if(path.StartsWith(cacheRoot,StringComparison.OrdinalIgnoreCase)&&!retained.Contains(path)&&File.Exists(path)) File.Delete(path);
        foreach(var id in ids) { var clipboard=Path.Combine(DataDirectory,"clipboard",id);if(Directory.Exists(clipboard)) Directory.Delete(clipboard,true); }
        SaveHistory();Images.ClearCompletedRequests();
        if(Groups.Count==0) _=ClearEmptyWorkspaceCachesAsync(ids.Count>0);
    }
}
