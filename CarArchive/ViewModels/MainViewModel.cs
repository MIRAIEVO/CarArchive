using System.Collections.ObjectModel;
using System.Text.Json;
using CarArchive.Core;
using CarArchive.Services;

namespace CarArchive.ViewModels;

public sealed partial class MainViewModel : Observable
{
    public static string DataDirectory=>PortableData.ResolveDirectory();
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
    private long selectionRevision;
    private bool busy;
    private string status="拖入照片开始整理。点击照片即设为代表图；不再需要确认分组。";
    public PhotoGroup? Selected { get=>selected; set { if(!ReferenceEquals(selected,value)) { selectionRevision++;if(selected is not null) selected.IsCurrent=false;if(Set(ref selected,value)) { if(value is not null) value.IsCurrent=true;CrashJournal.Trace("Group.Select",value?.Id);SelectionChanged(); } } } }
    public bool SelectedCanDeleteGroups => !Busy && Selected is not null && !Selected.IsWorking;
    public bool CanDeleteAllArchivedGroups => !Busy && Groups.Any(g=>g.Archived) && Groups.Where(g=>g.Archived).All(g=>!g.IsWorking);
    public bool Busy { get=>busy;set { if(Set(ref busy,value)) { Notify(nameof(Idle));Notify(nameof(SelectedCanDeleteGroups));Notify(nameof(CanDeleteAllArchivedGroups)); } } }
    public bool Idle=>!Busy;
    public bool SelectedEditable=>Selected?.Editable==true;
    public bool SelectedWorking=>Selected?.IsWorking==true;
    public bool SelectedCanRecognize=>SelectedEditable || Selected is {IsWorking:true,TaskKind:"联网识别"};
    public bool SelectedCanArchiveAction=>Selected is {IsWorking:false,FilesPending:false} group && (group.Archived || group.Editable);
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
    private void SelectionChanged() { Notify(nameof(SelectedEditable));Notify(nameof(SelectedWorking));Notify(nameof(SelectedCanRecognize));Notify(nameof(SelectedCanArchiveAction));Notify(nameof(SelectedCanDeleteGroups));Notify(nameof(Summary)); }
    private void Enqueue(Action action,bool fullSnapshot=false)
    {
        writes=writes.ContinueWith(_=> {
            try { action();if(fullSnapshot) writeError=null; }
            catch(Exception e) { CrashJournal.Trace("Persistence.Failed."+e.GetType().Name);writeError=e;ui?.Post(_=>Status="本地记录保存失败，已阻止后续归档："+e.Message,null);throw; }
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
        CrashJournal.Trace("Groups.Remove",Selected?.Id,targets.Count);
        if(targets.Count==0) throw new InvalidOperationException("请先在分组列表中选择要删除的分组。");
        if(targets.Any(g=>!Groups.Contains(g))) throw new InvalidOperationException("所选分组中有已不存在的项目，请重新选择。");
        if(targets.Any(g=>g.IsWorking)) throw new InvalidOperationException("正在执行任务的分组不能删除，请等待任务完成或取消。");
        var targetIds=targets.Select(g=>g.Id).ToHashSet(StringComparer.Ordinal);
        if(Store.Jobs().Any(j=>targetIds.Contains(j.GroupId) && j.Status is "Pending" or "Interrupted"))
            throw new InvalidOperationException("所选分组中有未完成的文件操作，请先从操作记录中恢复或撤销。");

        if(Database.PhotoOperations().Any(o=>PhotoLifecycleService.Unfinished(o) && (targetIds.Contains(o.GroupId)||targetIds.Contains(o.SourceGroupId)))) throw new InvalidOperationException("所选分组有未完成的照片文件操作，请先恢复或取消副本任务。");
        var firstIndex=targets.Min(Groups.IndexOf);
        var selectedWillBeRemoved=Selected is not null && targetIds.Contains(Selected.Id);
        foreach(var group in targets) Groups.Remove(group);
        PurgeArchivedHistory(targets);
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
        if(!group.IsWorking) { if(group.RepresentativeId!=photo.Id && group.Verification is not null) { group.VerificationHistory.Add(group.Verification);group.Verification=null; }group.RepresentativeId=photo.Id; MarkTouched(group); }
        CrashJournal.Trace("Photo.Select",group.Id);group.LastViewedId=photo.Id; SaveGroup(group);
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
            var started=System.Diagnostics.Stopwatch.StartNew();var metaWork=new Queue<Photo>(photos);var thumbWork=new Queue<Photo>(photos);
            async Task MetadataWorker() {while(metaWork.Count>0){ct.ThrowIfCancellationRequested();var photo=metaWork.Dequeue();await Images.ReadMetadataAsync(photo,ct);if(photo.Asset is not null)SaveAsset(photo.Asset);}}
            async Task ThumbnailWorker() {while(thumbWork.Count>0){ct.ThrowIfCancellationRequested();var photo=thumbWork.Dequeue();await Images.EnsureThumbnailAsync(photo,ct);if(photo.Asset is not null)SaveAsset(photo.Asset);foreach(var g in Groups.Where(g=>g.Photos.Any(p=>p.AssetId==photo.AssetId)))g.Refresh();PhotoUpdated?.Invoke(photo);}}
            var thumbnails=Task.WhenAll(ThumbnailWorker(),ThumbnailWorker());
            await Task.WhenAll(Enumerable.Range(0,8).Select(_=>MetadataWorker()));
            var metadataSeconds=started.Elapsed.TotalSeconds;Status=$"拍摄时间读取完成：{photos.Count} 个，{photos.Count/Math.Max(.001,metadataSeconds):F0} 个／秒；缩略图继续在后台生成。";
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
            foreach(var g in imported.Where(g=>Groups.Contains(g)&&!g.UserTouched&&!g.IsWorking&&g.Suggestion is null)){g.State="待识别";g.Refresh();}
            Save();await thumbnails;Save(); Status=$"后台缩略图已完成：{photos.Count} 张；解码失败 {photos.Count(p=>p.Error.Length>0)} 张。";
        }
        catch(OperationCanceledException) { Status="后台图片准备已停止，已登记的分组保留。";Save(); }
        catch(Exception e) { Status="后台图片处理失败："+e.Message;Save(); }
    }
    public async Task RunGroupAsync(PhotoGroup group,string kind,bool fileOperation,Func<CancellationToken,Task> action)
    {
        if(kind=="联网识别") {group.PendingUploadPhotos=UploadSelection.Select(group,State.Settings.UploadMultiple);group.UploadingPhotoIds=group.PendingUploadPhotos.Select(p=>p.Id).ToHashSet();}
        MarkTouched(group);
        CrashJournal.Trace("Task.Start."+kind,group.Id);
        try { await Tasks.RunAsync(group,kind,fileOperation,action,()=> { SaveGroup(group);GroupUpdated?.Invoke(group); },shutdown.Token); }
        finally { group.PendingUploadPhotos=null;group.UploadingPhotoIds.Clear();CrashJournal.Trace("Task.End."+group.TaskState,group.Id); }
    }
    public AppSettings CaptureSettings()=>JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(State.Settings,StateStore.Json),StateStore.Json)!;
    public async Task RecognizeAsync(PhotoGroup group,bool deep,CancellationToken ct,AppSettings? capturedSettings=null)
    {
        if(group.Archived) throw new InvalidOperationException("已归档组请先撤销。");
        var settings=capturedSettings ?? CaptureSettings();
        settings.Mode=group.Mode;settings.ExplicitUploadSelection=true;
        var photos=group.PendingUploadPhotos??UploadSelection.Select(group,State.Settings.UploadMultiple);group.UploadingPhotoIds=photos.Select(p=>p.Id).ToHashSet();
        if(deep)
        {
            if(group.Confirmed.Series.Length==0 && group.Confirmed.Variant.Length==0) throw new InvalidOperationException("请先联网识别或填写待查证车型。");
            var report=await Recognition.VerifyAsync(photos,settings,group.Confirmed.Clone(),group.Suggestion?.Uncertainties ?? "核对车系、代次、改款及具体版本是否有照片证据",ct);
            StoreVerification(group,report);group.State=report.StatusText;SaveGroup(group);
            if(report.Status==VerificationStatus.Canceled) throw new OperationCanceledException(ct);
            if(report.Status==VerificationStatus.Incomplete) throw new IOException(report.Error);
            return;
        }
        var result=await Recognition.RecognizeAsync(photos,settings,false,ct);
        ct.ThrowIfCancellationRequested();await PrepareNamingGuideAsync();ct.ThrowIfCancellationRequested();
        ApplyRecognitionResult(group,result);
        group.State="Gemini识别完成";
        SaveGroup(group);
    }
    public void ApplyRecognitionResult(PhotoGroup group,RecognitionResult result)
    {
        group.Suggestion=result;group.Confirmed=result.Identity.Clone();group.NamingOriginalIdentity=result.Identity.Clone();
        if(result.Verification is not null) StoreVerification(group,result.Verification);else group.Verification=null;
        group.ManualName=false;group.ManualTargetBrand=false;group.PreviousFolderName="";
        group.TargetBrand=Naming.ResolveBrand(group.Confirmed,State);GenerateName(group);
    }
    private static void StoreVerification(PhotoGroup group,VerificationReport report)
    {
        if(group.Verification is not null) group.VerificationHistory.Add(group.Verification);
        group.Verification=report;
    }
    public void ApplyVerification(PhotoGroup group)
    {
        RequireEditable(group);var report=group.Verification;
        if(report?.SuggestedIdentity is not { } identity || report.Status is VerificationStatus.Incomplete or VerificationStatus.Canceled or VerificationStatus.NotRun)
            throw new InvalidOperationException("没有可以采用的完整修正建议。");
        if(group.Confirmed.NamingKey!=report.Candidate.NamingKey) throw new InvalidOperationException("分类字段已改变，请重新查证后采用建议。");
        group.Confirmed=identity.Clone();
        if(!group.ManualTargetBrand) group.TargetBrand=Naming.ResolveBrand(group.Confirmed,State);
        if(!group.ManualName) { group.PreviousFolderName=group.FolderName;GenerateName(group); }
        else group.AppliedRule="已采用查证修正；保留手动名称，可重新生成";
        MarkTouched(group);group.State="已采用查证建议 · 待人工复核";SaveGroup(group);
    }
    public void GenerateName(PhotoGroup group)
    {
        var brand=group.Confirmed.Brand.Length>0?Naming.ResolveBrand(group.Confirmed,State):group.TargetBrand;
        var suggestion=Naming.Suggest(group.Confirmed,State,Naming.Guide?.Inventory.Where(entry=>entry.Brand.Equals(brand,StringComparison.OrdinalIgnoreCase)).Select(entry=>entry.Folder) ?? []);
        group.FolderName=suggestion.Name;group.LastGeneratedName=suggestion.Name;group.TargetBrand=group.ManualTargetBrand?group.TargetBrand:brand;group.AppliedRule=suggestion.Reason;group.ManualName=false;group.Refresh();
    }
    private void RequireEditable(PhotoGroup group)
    {
        if(!group.Editable) throw new InvalidOperationException("该组正在执行任务或已归档，请稍后操作。");
        if(Database.PhotoOperations().Any(o=>PhotoLifecycleService.Unfinished(o) && (o.GroupId==group.Id || o.SourceGroupId==group.Id))) throw new InvalidOperationException("本组有中断的照片操作，请先恢复或放弃副本准备。");
        if(Store.Jobs().Any(j=>j.GroupId==group.Id && j.Status is "Pending" or "Interrupted")) throw new InvalidOperationException("该组有中断的文件操作，请先恢复或撤销后再拆分、合并或复制。");
    }
    public void Split(PhotoGroup group,IReadOnlyList<Photo> chosen)
    {
        CrashJournal.Trace("Photos.Split",group.Id,chosen.Count);
        if(chosen.Any(p=>!group.Photos.Contains(p))) throw new InvalidOperationException("所选照片不属于当前组。");
        if(group.IsWorking && group.TaskKind=="联网识别") { if(chosen.Any(p=>group.UploadingPhotoIds.Contains(p.Id))) throw new InvalidOperationException("不能拆分正在上传的照片。"); } else RequireEditable(group);if(chosen.Count==0 || chosen.Count==group.Photos.Count) throw new InvalidOperationException("请选中需要拆出的部分照片。");
        var newGroup=new PhotoGroup { Photos=new(chosen),UserTouched=true,State="待识别" };
        foreach(var p in chosen) group.Photos.Remove(p);
        group.UserTouched=true;group.Revision++;group.Suggestion=null;if(group.Verification is not null) group.VerificationHistory.Add(group.Verification);group.Verification=null;group.State="分组已改变";
        Groups.Insert(Groups.IndexOf(group)+1,newGroup); Selected=newGroup;Save();StructureUpdated?.Invoke();
    }
    public void Merge(IReadOnlyList<PhotoGroup> groups)
    {
        CrashJournal.Trace("Groups.Merge",Selected?.Id,groups.Count);
        if(groups.Count<2) throw new InvalidOperationException("请多选至少两个分组。");
        foreach(var g in groups) RequireEditable(g);
        var ordered=groups.OrderBy(g=>Groups.IndexOf(g)).ToList();
        var merged=GroupMergePolicy.Choose(ordered);var index=Groups.IndexOf(ordered[0]);
        merged.Photos=new(GroupingService.Sort(ordered.SelectMany(g=>g.Photos)));
        merged.UserTouched=true;merged.Revision++;
        foreach(var g in ordered) Groups.Remove(g);
        Groups.Insert(Math.Min(index,Groups.Count),merged);Selected=merged;Save();StructureUpdated?.Invoke();
    }
    public void RemovePhotos(PhotoGroup group,IReadOnlyList<Photo> selected)
    {
        RequireEditable(group);var chosen=GroupPhotoEditor.Validate(group,selected);
        CrashJournal.Trace("Photos.RemoveReferences",group.Id,chosen.Count);
        if(chosen.Count==group.Photos.Count) { RemoveGroups([group]);return; }
        foreach(var photo in chosen) group.Photos.Remove(photo);
        if(!group.Photos.Any(p=>p.Id==group.RepresentativeId)) group.RepresentativeId=group.Photos[0].Id;
        if(!group.Photos.Any(p=>p.Id==group.LastViewedId)) group.LastViewedId=group.RepresentativeId;
        group.Suggestion=null;if(group.Verification is not null) group.VerificationHistory.Add(group.Verification);group.Verification=null;group.State="照片已移除 · 请复核";MarkTouched(group);SaveGroup(group);StructureUpdated?.Invoke();
    }
    public IReadOnlyList<LocalNamingRule> Learn(PhotoGroup group,bool explicitly)
    {
        if(State.Settings.Learning==LearningMode.Manual && !explicitly) return [];
        if(explicitly) {var entry=new NamingClickLog{GroupId=group.Id,Original=(group.NamingOriginalIdentity??group.Suggestion?.Identity??group.Confirmed).Clone(),Confirmed=group.Confirmed.Clone(),AutomaticName=group.LastGeneratedName,FinalName=group.FolderName,Rule=group.AppliedRule};Enqueue(()=>Database.RecordNamingClick(entry));}
        Naming.Remember(group.Confirmed,group.FolderName,State);
        if(group.Confirmed.Brand.Length>0 && group.TargetBrand.Length>0 && NamingService.SameBrand(group.Confirmed.Brand,group.TargetBrand)) State.BrandFolders[group.Confirmed.Brand]=NamingService.SafeSegment(group.TargetBrand);
        var pending=LocalNamingLearning.RecordConfirmed(group,State,explicitly);SaveHeader();return pending;
    }
    public async Task ArchiveAsync(PhotoGroup group,CancellationToken ct,string? capturedRoot=null,string? capturedBrand=null,string? capturedFolder=null)
    {
        if(group.Archived) throw new InvalidOperationException("本组已经归档。");
        if(group.FilesPending || Database.PhotoOperations().Any(o=>PhotoLifecycleService.Unfinished(o) && (o.GroupId==group.Id || o.SourceGroupId==group.Id))) throw new InvalidOperationException("本组照片操作尚未完成，请先恢复后再归档。");
        if(Store.Jobs().Any(j=>j.GroupId==group.Id && j.Status is "Pending" or "Interrupted")) throw new InvalidOperationException("本组有中断操作，请从操作记录恢复。");
        var selectedRevision=selectionRevision;var root=capturedRoot ?? State.Settings.ArchiveRoot;var brand=capturedBrand??group.TargetBrand;var folder=capturedFolder??group.FolderName;
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
        if(Selected==group && selectionRevision==selectedRevision)
        {
            var i=Groups.IndexOf(group);Selected=Groups.Skip(i+1).FirstOrDefault(g=>!g.Archived) ?? Groups.FirstOrDefault(g=>!g.Archived) ?? group;
            StructureUpdated?.Invoke();
        }
        Status="已归档；共享原图将在所有使用它的分组归档后删除。";
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
    public async Task ClearNamingDataAsync()
    {
        if(Groups.Any(g=>g.IsWorking))throw new InvalidOperationException("请等待当前任务结束后再清除学习数据。");
        await FlushWritesAsync();State.Names.Clear();State.Rules.Clear();State.NamingSamples.Clear();State.LocalNamingRules.Clear();SaveHeader();await FlushWritesAsync();Database.ClearNamingClicks();
    }
    public string ExportNamingClicksCsv()=>Database.ExportNamingClicksCsv();
    public void Close()
    {
        shutdown.Cancel();Tasks.CancelAll();Cancellation?.Cancel();Save();
        try { writes.GetAwaiter().GetResult(); } catch { }
    }
}
