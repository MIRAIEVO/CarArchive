using System.Collections.ObjectModel;
using System.Text.Json;
using CarArchive.Core;
using CarArchive.Services;

namespace CarArchive.ViewModels;

public sealed class MainViewModel : Observable
{
    public static string DataDirectory=>Environment.GetEnvironmentVariable("CARARCHIVE_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CarArchive");
    public WorkspaceState State { get; }
    public ObservableCollection<PhotoGroup> Groups { get; }
    public IStateStore Store { get; }
    private StateStore Database=>(StateStore)Store;
    public CredentialService Credentials { get; }
    public PhotoService Images { get; }
    public GeminiService Gemini { get; }
    public IRecognitionService Recognition { get; set; }
    public NamingService Naming { get; }=new();
    public ArchiveService Archive { get; }
    public GroupTaskCoordinator Tasks { get; }=new();
    private readonly HttpClient http=new() { Timeout=TimeSpan.FromMinutes(4) };
    private readonly CancellationTokenSource shutdown=new();
    private Task writes=Task.CompletedTask;
    private Exception? writeError;
    private readonly SynchronizationContext? ui=SynchronizationContext.Current;
    public Task BackgroundImport { get; private set; }=Task.CompletedTask;
    public event Action<PhotoGroup>? GroupUpdated;
    public event Action<Photo>? PhotoUpdated;
    public event Action? StructureUpdated;
    private PhotoGroup? selected;
    private bool busy;
    private string status="拖入照片开始整理。点击照片即设为代表图；不再需要确认分组。";
    public PhotoGroup? Selected { get=>selected; set { if(Set(ref selected,value)) SelectionChanged(); } }
    public bool SelectedCanDeleteGroups => !Busy && Selected is not null && !Selected.IsWorking;
    public bool CanDeleteAllArchivedGroups => !Busy && Groups.Any(g=>g.Archived) && Groups.Where(g=>g.Archived).All(g=>!g.IsWorking);
    public bool Busy { get=>busy;set { if(Set(ref busy,value)) { Notify(nameof(Idle));Notify(nameof(SelectedCanDeleteGroups));Notify(nameof(CanDeleteAllArchivedGroups)); } } }
    public bool Idle=>!Busy;
    public bool SelectedEditable=>Selected?.Editable==true;
    public bool SelectedWorking=>Selected?.IsWorking==true;
    public string Status { get=>status;set=>Set(ref status,value); }
    public string Summary=>$"{Groups.Count(g=>!g.Archived)} 组待处理 · {Groups.Sum(g=>g.Photos.Count)} 张 · 本批请求 {State.BatchCalls}/{State.Settings.BatchCallLimit}";
    public CancellationTokenSource? Cancellation { get; set; }
    public MainViewModel()
    {
        Store=new StateStore(DataDirectory); State=Store.Load(); ArchiveService.Reconcile(State,Store.Jobs());
        Groups=new(State.Groups); Credentials=new(DataDirectory); Images=new(Path.Combine(DataDirectory,"cache"));
        Gemini=new(http,Images,Credentials.Read,ReserveCall); Recognition=Gemini; Archive=new(Store);
        Selected=Groups.FirstOrDefault(g=>!g.Archived) ?? Groups.FirstOrDefault();
        if(Store.Jobs().Any(j=>j.Status is "Pending" or "Interrupted")) Status="存在中断操作，请在“操作记录”中检查并恢复。";
    }
    private void SelectionChanged() { Notify(nameof(SelectedEditable));Notify(nameof(SelectedWorking));Notify(nameof(SelectedCanDeleteGroups));Notify(nameof(Summary)); }
    private void Enqueue(Action action,bool fullSnapshot=false)
    {
        writes=writes.ContinueWith(_=> {
            try { action();if(fullSnapshot) writeError=null; }
            catch(Exception e) { writeError=e;ui?.Post(_=>Status="本地记录保存失败，已阻止后续归档："+e.Message,null);throw; }
        },CancellationToken.None,TaskContinuationOptions.None,TaskScheduler.Default);
    }
    public async Task FlushWritesAsync() { await writes;if(writeError is not null) throw new IOException("本地记录未安全保存，请检查磁盘空间后重试保存。",writeError); }
    public void Save()
    {
        State.Groups=Groups.ToList(); Enqueue(Database.CaptureSave(State),true); Notify(nameof(Summary));Notify(nameof(CanDeleteAllArchivedGroups));
    }
    public void SaveGroup(PhotoGroup group)
    {
        group.Refresh(); Enqueue(Database.CaptureGroup(group)); Notify(nameof(Summary));
        Notify(nameof(CanDeleteAllArchivedGroups));
        if(ReferenceEquals(group,Selected)) SelectionChanged();
    }
    public void RemoveGroups(IReadOnlyCollection<PhotoGroup> groups)
    {
        var targets=groups.Distinct().ToList();
        if(targets.Count==0) throw new InvalidOperationException("请先在分组列表中选择要删除的分组。");
        if(targets.Any(g=>!Groups.Contains(g))) throw new InvalidOperationException("所选分组中有已不存在的项目，请重新选择。");
        if(targets.Any(g=>g.IsWorking)) throw new InvalidOperationException("正在执行任务的分组不能删除，请等待任务完成或取消。");
        var targetIds=targets.Select(g=>g.Id).ToHashSet(StringComparer.Ordinal);
        if(Store.Jobs().Any(j=>targetIds.Contains(j.GroupId) && j.Status is "Pending" or "Interrupted"))
            throw new InvalidOperationException("所选分组中有未完成的文件操作，请先从操作记录中恢复或撤销。");

        var firstIndex=targets.Min(Groups.IndexOf);
        var selectedWillBeRemoved=Selected is not null && targetIds.Contains(Selected.Id);
        foreach(var group in targets) Groups.Remove(group);
        if(selectedWillBeRemoved)
            Selected=Groups.Skip(Math.Min(firstIndex,Groups.Count)).FirstOrDefault(g=>!g.Archived)
                ?? Groups.FirstOrDefault(g=>!g.Archived)
                ?? Groups.Skip(Math.Min(firstIndex,Groups.Count)).FirstOrDefault()
                ?? Groups.FirstOrDefault();
        Save();
        StructureUpdated?.Invoke();
        Status=$"已移除 {targets.Count} 个分组记录；源照片和已归档目录均未删除或更改。";
    }
    public void RemoveAllArchivedGroups()
    {
        var archived=Groups.Where(g=>g.Archived).ToList();
        if(archived.Count==0) throw new InvalidOperationException("没有已归档分组。");
        RemoveGroups(archived);
    }
    public void SaveHeader() { Enqueue(Database.CaptureHeader(State));Notify(nameof(Summary)); }
    public void SaveAsset(ImageAsset asset)=>Enqueue(Database.CaptureAsset(asset));
    private void ReserveCall()
    {
        if(State.BatchCalls>=State.Settings.BatchCallLimit) throw new InvalidOperationException("本批请求已达到上限，可在设置调整或重置。");
        State.BatchCalls++; SaveHeader();
    }
    public void MarkTouched(PhotoGroup group) { group.UserTouched=true; group.Revision++; }
    public void SelectPhoto(PhotoGroup group,Photo photo)
    {
        if(!group.IsWorking) { group.RepresentativeId=photo.Id; MarkTouched(group); }
        group.LastViewedId=photo.Id; SaveGroup(group);
    }
    public async Task ImportAsync(IEnumerable<string> paths,bool singleGroup,CancellationToken ct)
    {
        var known=Groups.SelectMany(g=>g.Photos).Select(p=>Path.GetFullPath(p.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files=await Task.Run(()=>paths.Where(p=>PhotoService.Extensions.Contains(Path.GetExtension(p))).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Where(p=>!known.Contains(p)).ToList(),ct);
        if(files.Count==0) { Status="没有新的受支持照片。";return; }
        var photos=new List<Photo>();
        foreach(var file in files) { ct.ThrowIfCancellationRequested(); var pair=PhotoService.Register(file); State.Assets.Add(pair.Asset);photos.Add(pair.Reference); }
        var batchId=Guid.NewGuid().ToString("N");
        var imported=singleGroup?new List<PhotoGroup>{new() { Photos=new(photos),UserTouched=true,State="待识别",ImportBatchId=batchId }}
            :photos.Select(p=>new PhotoGroup { Photos=[p],ImportBatchId=batchId,State="正在读取拍摄信息" }).ToList();
        foreach(var group in imported) Groups.Add(group);
        State.BatchId=batchId;State.BatchCalls=0; Selected=imported[0];Save();
        Status=$"已登记 {files.Count} 张，可立即操作。缩略图将在后台逐步显示。";
        StructureUpdated?.Invoke();
        BackgroundImport=Task.WhenAll(BackgroundImport,PrepareImportAsync(photos,imported,singleGroup,shutdown.Token));
    }
    private async Task PrepareImportAsync(List<Photo> photos,List<PhotoGroup> imported,bool singleGroup,CancellationToken ct)
    {
        try
        {
            var work=new Queue<Photo>(photos);
            async Task Worker()
            {
                while(work.Count>0)
                {
                    ct.ThrowIfCancellationRequested(); var photo=work.Dequeue();
                    await Images.ReadMetadataAsync(photo,ct);
                    await Images.EnsureThumbnailAsync(photo,ct);
                    if(photo.Asset is not null) SaveAsset(photo.Asset);
                    foreach(var group in Groups.Where(g=>g.Photos.Any(p=>p.AssetId==photo.AssetId)))
                    {
                        if(!group.UserTouched && group.Suggestion is null && !group.IsWorking) group.State=photo.Error.Length>0?"无法解码 · 可检查原图":"待识别";
                        group.Refresh();
                    }
                    PhotoUpdated?.Invoke(photo);
                }
            }
            await Task.WhenAll(Worker(),Worker());
            if(!singleGroup)
            {
                var untouched=imported.Where(g=>Groups.Contains(g) && !g.UserTouched && !g.IsWorking && !g.Archived && g.Suggestion is null).ToList();
                var selectionWasUntouched=Selected is not null && untouched.Contains(Selected);
                if(untouched.Count>0)
                {
                    var position=Groups.IndexOf(untouched[0]);
                    var regrouped=GroupingService.Suggest(untouched.SelectMany(g=>g.Photos),State.Settings.GroupGapSeconds);
                    var unchanged=regrouped.Count==untouched.Count && regrouped.Zip(untouched).All(pair=>pair.First.Photos.Select(p=>p.Id).SequenceEqual(pair.Second.Photos.Select(p=>p.Id)));
                    if(!unchanged)
                    {
                        foreach(var g in untouched) Groups.Remove(g);
                        foreach(var g in regrouped) { g.ImportBatchId=imported[0].ImportBatchId;g.State="待识别";Groups.Insert(Math.Min(position++,Groups.Count),g); }
                        if(selectionWasUntouched) Selected=regrouped.FirstOrDefault();
                        StructureUpdated?.Invoke();
                    }
                }
            }
            Save(); Status=$"后台缩略图已完成：{photos.Count} 张；解码失败 {photos.Count(p=>p.Error.Length>0)} 张。";
        }
        catch(OperationCanceledException) { Status="后台图片准备已停止，已登记的分组保留。";Save(); }
        catch(Exception e) { Status="后台图片处理失败："+e.Message;Save(); }
    }
    public async Task RunGroupAsync(PhotoGroup group,string kind,bool fileOperation,Func<CancellationToken,Task> action)
    {
        MarkTouched(group);
        await Tasks.RunAsync(group,kind,fileOperation,action,()=> { SaveGroup(group);GroupUpdated?.Invoke(group); },shutdown.Token);
    }
    public AppSettings CaptureSettings()=>JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(State.Settings,StateStore.Json),StateStore.Json)!;
    public async Task RecognizeAsync(PhotoGroup group,bool deep,CancellationToken ct,AppSettings? capturedSettings=null)
    {
        if(group.Archived) throw new InvalidOperationException("已归档组请先撤销。");
        var settings=capturedSettings ?? CaptureSettings();
        var photos=group.Photos.Where(p=>!p.Excluded).OrderByDescending(p=>p.Id==group.RepresentativeId).Select(p=>p.Snapshot()).ToList();
        var result=await Recognition.RecognizeAsync(photos,settings,deep,ct);
        if(!deep && settings.Mode==RecognitionMode.Accurate && (!result.Grounded || result.Uncertainties.Length>0 || result.Alternatives.Count>0) && State.BatchCalls<State.Settings.BatchCallLimit)
        {
            try {
                var second=await Recognition.RecognizeAsync(photos,settings,true,ct);
                if(second.Identity.Key!=result.Identity.Key) { second.Conflict=true;second.Uncertainties+="\n两次判断不一致，请人工核对。"; }
                second.InputTokens+=result.InputTokens;second.OutputTokens+=result.OutputTokens;
                second.ThinkingTokens=second.ThinkingTokens.HasValue && result.ThinkingTokens.HasValue?second.ThinkingTokens+result.ThinkingTokens:null;
                second.SearchCount=second.SearchCount.HasValue && result.SearchCount.HasValue?second.SearchCount+result.SearchCount:null;
                second.Queries=result.Queries.Concat(second.Queries).Distinct().ToList();second.Sources=result.Sources.Concat(second.Sources).Distinct().ToList();result=second;
            } catch(OperationCanceledException) { throw; }
            catch(Exception e) { result.Conflict=true;result.Uncertainties+="\n追加核验未完成："+e.Message; }
        }
        ct.ThrowIfCancellationRequested();
        group.Suggestion=result;group.Confirmed=result.Identity.Clone();
        group.PreviousFolderName=group.FolderName; GenerateName(group);
        group.State=result.Grounded?"已返回联网结果 · 待人工复核":"未完成查证 · 可人工复核";
        SaveGroup(group);
    }
    public void GenerateName(PhotoGroup group)
    {
        var brand=State.BrandFolders.FirstOrDefault(p=>p.Key.Equals(group.Confirmed.Brand,StringComparison.OrdinalIgnoreCase)).Value ?? (group.TargetBrand.Length>0?group.TargetBrand:group.Confirmed.Brand);
        var suggestion=Naming.Suggest(group.Confirmed,State,NamingService.ExistingNames(State.Settings.ArchiveRoot,brand));
        group.FolderName=suggestion.Name;group.LastGeneratedName=suggestion.Name;group.TargetBrand=brand;group.AppliedRule=suggestion.Reason;group.ManualName=false;group.Refresh();
    }
    private void RequireEditable(PhotoGroup group)
    {
        if(!group.Editable) throw new InvalidOperationException("该组正在执行任务或已归档，请稍后操作。");
        if(Store.Jobs().Any(j=>j.GroupId==group.Id && j.Status is "Pending" or "Interrupted")) throw new InvalidOperationException("该组有中断的文件操作，请先恢复或撤销后再拆分、合并或复制。");
    }
    public void Split(PhotoGroup group,IReadOnlyList<Photo> chosen)
    {
        RequireEditable(group);if(chosen.Count==0 || chosen.Count==group.Photos.Count) throw new InvalidOperationException("请选中需要拆出的部分照片。");
        var newGroup=new PhotoGroup { Photos=new(chosen),UserTouched=true,State="待识别" };
        foreach(var p in chosen) group.Photos.Remove(p);
        group.UserTouched=true;group.Revision++;group.Suggestion=null;group.State="分组已改变";
        Groups.Insert(Groups.IndexOf(group)+1,newGroup); Selected=newGroup;Save();StructureUpdated?.Invoke();
    }
    public void Merge(IReadOnlyList<PhotoGroup> groups)
    {
        if(groups.Count<2) throw new InvalidOperationException("请多选至少两个分组。");
        foreach(var g in groups) RequireEditable(g);
        var merged=new PhotoGroup { Photos=new(GroupingService.Sort(groups.SelectMany(g=>g.Photos))),UserTouched=true,State="待识别" };
        var index=Groups.IndexOf(groups[0]);foreach(var g in groups) Groups.Remove(g);
        Groups.Insert(Math.Min(index,Groups.Count),merged);Selected=merged;Save();StructureUpdated?.Invoke();
    }
    public PhotoGroup Duplicate(PhotoGroup group)
    {
        RequireEditable(group);
        MarkTouched(group);
        var copy=new PhotoGroup { Photos=new(group.Photos.Select(p=>p.CopyReference(true))),UserTouched=true,State="复制组 · 可框选另一辆车" };
        copy.RepresentativeId=copy.Photos.FirstOrDefault(p=>p.AssetId==group.Photos.FirstOrDefault(x=>x.Id==group.RepresentativeId)?.AssetId)?.Id ?? copy.Photos.FirstOrDefault()?.Id ?? "";
        Groups.Insert(Groups.IndexOf(group)+1,copy);Selected=copy;Save();StructureUpdated?.Invoke();return copy;
    }
    public NamingRule? Learn(PhotoGroup group,bool explicitly)
    {
        if(State.Settings.Learning==LearningMode.Manual && !explicitly) return null;
        Naming.Remember(group.Confirmed,group.FolderName,State);
        if(group.Confirmed.Brand.Length>0 && group.TargetBrand.Length>0) State.BrandFolders[group.Confirmed.Brand]=NamingService.SafeSegment(group.TargetBrand);
        var rule=Naming.ProposeRule(group.LastGeneratedName,group.FolderName,group.Confirmed.Brand);
        if(rule is not null && !State.Rules.Any(r=>r.Brand==rule.Brand && r.From==rule.From && r.To==rule.To))
        { if(State.Settings.Learning==LearningMode.Automatic) { State.Rules.Add(rule);rule=null; } } else rule=null;
        SaveHeader();return rule;
    }
    public async Task ArchiveAsync(PhotoGroup group,CancellationToken ct,string? capturedRoot=null)
    {
        if(group.Archived) throw new InvalidOperationException("本组已经归档。");
        if(Store.Jobs().Any(j=>j.GroupId==group.Id && j.Status is "Pending" or "Interrupted")) throw new InvalidOperationException("本组有中断操作，请从操作记录恢复。");
        var root=capturedRoot ?? State.Settings.ArchiveRoot;var brand=group.TargetBrand;var folder=group.FolderName;
        // Freeze the source/reference set before leaving the UI thread.
        var snapshot=new PhotoGroup { Id=group.Id,Photos=new(group.Photos.Select(p=>p.Snapshot())) };
        var references=new WorkspaceState { Groups=Groups.Select(g=>new PhotoGroup { Id=g.Id,Archived=g.Archived,Photos=new(g.Photos.Select(p=>new Photo { AssetId=p.AssetId,Excluded=p.Excluded })) }).ToList() };
        await FlushWritesAsync();
        try {
            await Task.Run(async ()=> { var job=await Archive.PlanAsync(snapshot,root,brand,folder,ct,references);await Archive.ExecuteAsync(job,ct); },ct);
        } finally {
            var jobs=await Task.Run(Store.Jobs);
            ArchiveService.Reconcile(new WorkspaceState { Groups=[group] },jobs);SaveGroup(group);
        }
        if(Selected==group)
        {
            var i=Groups.IndexOf(group);Selected=Groups.Skip(i+1).FirstOrDefault(g=>!g.Archived) ?? Groups.FirstOrDefault(g=>!g.Archived) ?? group;
            StructureUpdated?.Invoke();
        }
        Status="已归档；共享原图将在所有使用它的分组归档后删除。";
    }
    public async Task VisualGroupAsync(CancellationToken ct)
    {
        var targets=Groups.Where(g=>g.Editable && g.Suggestion is null).ToList();
        var photos=GroupingService.Sort(targets.SelectMany(g=>g.Photos).Where(p=>!p.Excluded));
        if(photos.Count==0) throw new InvalidOperationException("没有可重新分组的照片。");
        foreach(var g in targets) { MarkTouched(g);g.TaskKind="视觉分组";g.TaskState=GroupTaskState.Running;SaveGroup(g); }
        try {
            var adjacent=new HashSet<(string,string)>();
            for(var start=0;start<photos.Count;start+=22)
            {
                var batch=photos.Skip(start).Take(24).ToList();var settings=CaptureSettings();var suggestions=await Tasks.WithRecognitionSlotAsync(token=>Recognition.GroupAsync(batch,settings,token),ct);
                foreach(var ids in suggestions) { var set=ids.ToHashSet();for(var i=1;i<batch.Count;i++) if(set.Contains(batch[i-1].Id)&&set.Contains(batch[i].Id)) adjacent.Add((batch[i-1].Id,batch[i].Id)); }
                if(start+24>=photos.Count) break;
            }
            var proposed=new List<List<string>>();for(var i=0;i<photos.Count;i++) { if(i==0 || !adjacent.Contains((photos[i-1].Id,photos[i].Id))) proposed.Add([]);proposed[^1].Add(photos[i].Id); }
            var groups=GroupingService.FromVisual(photos,proposed);
            var excluded=targets.SelectMany(g=>g.Photos).Where(p=>p.Excluded).ToList();if(excluded.Count>0) groups.Add(new() { Photos=new(excluded),State="已排除照片" });
            var replaceSelection=Selected is not null && targets.Contains(Selected);
            foreach(var g in targets) Groups.Remove(g);
            foreach(var g in groups) { g.UserTouched=true;g.State="待识别";Groups.Add(g); }
            if(replaceSelection) Selected=groups.FirstOrDefault();
            Save();StructureUpdated?.Invoke();Status="视觉分组完成，当前显示的分组可直接识别。";
        } finally { foreach(var g in targets.Where(Groups.Contains)) { g.TaskState=GroupTaskState.Idle;SaveGroup(g); } }
    }
    public void Navigate(int delta) { if(Groups.Count>0) Selected=Groups[Math.Clamp((Selected is null?0:Groups.IndexOf(Selected))+delta,0,Groups.Count-1)]; }
    public async Task UndoLastAsync(CancellationToken ct)
    {
        var job=Store.Jobs().FirstOrDefault(j=>j.Direction=="Archive" && j.Status=="Completed") ?? throw new InvalidOperationException("没有可撤销的归档。");
        await ResumeOrUndoAsync(job,true,ct);
    }
    public async Task ResumeOrUndoAsync(ArchiveJob job,bool undo,CancellationToken ct)
    {
        var group=Groups.FirstOrDefault(g=>g.Id==job.GroupId);
        try { await Task.Run(async ()=> { if(undo) await Archive.UndoAsync(job,ct);else await Archive.ExecuteAsync(job,ct); },ct); }
        finally { if(group is not null) { ArchiveService.Reconcile(new WorkspaceState { Groups=[group] },await Task.Run(Store.Jobs));SaveGroup(group);GroupUpdated?.Invoke(group); } }
        Status=undo?"已撤销所选组，其他组的副本保持不变。":"中断操作已恢复。";
    }
    public void Close()
    {
        shutdown.Cancel();Tasks.CancelAll();Cancellation?.Cancel();Save();
        try { writes.GetAwaiter().GetResult(); } catch { }
    }
}
