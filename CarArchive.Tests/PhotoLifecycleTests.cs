using System.Text.Json;
using CarArchive.Core;

static class PhotoLifecycleTests
{
    private static void Check(bool value,string message="Photo lifecycle assertion failed") { if(!value) throw new Exception(message); }
    private static async Task Expect<T>(Func<Task> action) where T:Exception { try { await action(); }catch(T) { return; }throw new Exception("Expected "+typeof(T).Name); }
    private static string Area() { var p=Path.Combine(Path.GetTempPath(),"CarArchive-photo-lifecycle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p; }
    private static async Task<Photo> PhotoAt(string directory,string name="IMG_0001.jpg")
    { Directory.CreateDirectory(directory);var path=Path.Combine(directory,name);await File.WriteAllBytesAsync(path,Enumerable.Range(0,4096).Select(i=>(byte)(i%251)).ToArray());return new Photo { Path=path,OriginalPath=path,AssetId=Guid.NewGuid().ToString("N") }; }
    private static PhotoGroup Group(params Photo[] photos)=>new() { Photos=new(photos),UserTouched=true };
    private sealed class Store:IPhotoOperationStore
    {
        public List<PhotoFileOperation> Saved=[];
        public void SavePhotoOperation(PhotoFileOperation operation) { var copy=JsonSerializer.Deserialize<PhotoFileOperation>(JsonSerializer.Serialize(operation,StateStore.Json),StateStore.Json)!;Saved.RemoveAll(x=>x.Id==operation.Id);Saved.Add(copy); }
        public List<PhotoFileOperation> PhotoOperations()=>Saved;
    }
    private sealed class Recycler(string directory):IPhotoRecycler
    {
        public int Calls;public string? FailName;public bool Unknown;
        public Task<string> RecycleAsync(PhotoFileEntry entry,CancellationToken ct)
        { ct.ThrowIfCancellationRequested();Calls++;if(Path.GetFileName(entry.Source)==FailName) throw new PhotoRecycleRejectedException("Synthetic unavailable recycle bin");Directory.CreateDirectory(directory);var target=Path.Combine(directory,Guid.NewGuid().ToString("N")+"-"+Path.GetFileName(entry.Source));File.Move(entry.Source,target);if(Unknown) throw new IOException("Synthetic unknown result after dispatch");return Task.FromResult(target); }
    }
    private sealed class CancelProgress(CancellationTokenSource source):IProgress<string> { public void Report(string value) { if(value.StartsWith("复制并校验 2/")) source.Cancel(); } }
    public static void Register(Action<string,Func<Task>> test)
    {
        test("Independent copy preserves original bytes/name, creates a separate asset, and never changes other group crop",async ()=>
        {
            var root=Area();var photo=await PhotoAt(root);photo.Crop=new(.1,.2,.3,.4);var original=Group(photo);var copy=GroupPhotoEditor.Copy(original,[photo]);var store=new Store();var service=new PhotoLifecycleService(store,Path.Combine(root,"managed"),new Recycler(Path.Combine(root,"recycle")));
            var before=await ArchiveService.HashAsync(photo.Path);var operation=await service.PlanCopyAsync(copy,original.Id,"Copy",default);await service.CopyAsync(operation,null,default);var state=new WorkspaceState { Groups=[original,copy] };PhotoLifecycleService.AttachCopies(copy,state,operation);service.CompleteCopy(operation);
            Check(original.Photos[0].Path!=copy.Photos[0].Path && original.Photos[0].AssetId!=copy.Photos[0].AssetId);
            Check(Path.GetFileName(copy.Photos[0].Path)==photo.Name && await ArchiveService.HashAsync(copy.Photos[0].Path)==before && await ArchiveService.HashAsync(photo.Path)==before);
            Check(photo.Crop is not null && copy.Photos[0].Crop is null && operation.Status=="Completed" && File.Exists(photo.Path));
        });
        test("Copy detects a changed source before creating a valid destination",async ()=>
        {
            var root=Area();var photo=await PhotoAt(root);var service=new PhotoLifecycleService(new Store(),Path.Combine(root,"managed"),new Recycler(Path.Combine(root,"recycle")));var operation=await service.PlanCopyAsync(Group(photo),"source","Copy",default);
            await File.AppendAllTextAsync(photo.Path,"changed");await Expect<IOException>(()=>service.CopyAsync(operation,null,default));Check(operation.Status=="Interrupted" && !File.Exists(operation.Entries[0].Destination) && File.Exists(photo.Path));
        });
        test("Copy resumes the verified rename crash window and reuses identical completed output without overwriting",async ()=>
        {
            var root=Area();var photo=await PhotoAt(root);var service=new PhotoLifecycleService(new Store(),Path.Combine(root,"managed"),new Recycler(Path.Combine(root,"recycle")));var operation=await service.PlanCopyAsync(Group(photo),"source","Copy",default);await service.CopyAsync(operation,null,default);
            operation.Entries[0].Stage="Verified";await service.CopyAsync(operation,null,default);Check(operation.Entries[0].Stage=="Ready");
            await File.AppendAllTextAsync(operation.Entries[0].Destination,"user change");await Expect<IOException>(()=>service.CopyAsync(operation,null,default));Check(File.ReadAllText(operation.Entries[0].Destination).EndsWith("user change"));
        });
        test("Copy cancellation cleans only its verified managed outputs and leaves source originals intact",async ()=>
        {
            var root=Area();var a=await PhotoAt(root,"a.jpg");var b=await PhotoAt(root,"b.jpg");var service=new PhotoLifecycleService(new Store(),Path.Combine(root,"managed"),new Recycler(Path.Combine(root,"recycle")));var operation=await service.PlanCopyAsync(Group(a,b),"source","Copy",default);using var cancel=new CancellationTokenSource();
            await Expect<OperationCanceledException>(()=>service.CopyAsync(operation,new CancelProgress(cancel),cancel.Token));await service.AbandonCopyAsync(operation,default);
            Check(operation.Status=="Canceled" && File.Exists(a.Path) && File.Exists(b.Path) && operation.Entries.All(e=>!File.Exists(e.Destination) && !File.Exists(e.Temporary)));
        });
        test("Attachment validates all entries before mutating the first photo or workspace assets",async ()=>
        {
            var root=Area();var a=await PhotoAt(root,"a.jpg");var b=await PhotoAt(root,"b.jpg");var group=Group(a,b);var service=new PhotoLifecycleService(new Store(),Path.Combine(root,"managed"),new Recycler(Path.Combine(root,"recycle")));var operation=await service.PlanCopyAsync(group,"source","Copy",default);await service.CopyAsync(operation,null,default);operation.Entries[1].PhotoIds=["not-in-group"];var state=new WorkspaceState();
            await Expect<IOException>(()=> { PhotoLifecycleService.AttachCopies(group,state,operation);return Task.CompletedTask; });Check(a.Path==a.OriginalPath && !a.Path.Contains("managed") && state.Assets.Count==0);
        });
        test("Managed copy traversal and non-owned destination collisions are rejected",async ()=>
        {
            var root=Area();var photo=await PhotoAt(root);var service=new PhotoLifecycleService(new Store(),Path.Combine(root,"managed"),new Recycler(Path.Combine(root,"recycle")));var operation=await service.PlanCopyAsync(Group(photo),"source","Copy",default);operation.Entries[0].Destination=Path.Combine(root,"outside.jpg");await Expect<IOException>(()=>service.CopyAsync(operation,null,default));Check(!File.Exists(operation.Entries[0].Destination));
            var second=await service.PlanCopyAsync(Group(photo),"source","Copy",default);Directory.CreateDirectory(Path.GetDirectoryName(second.Entries[0].Destination)!);await File.WriteAllTextAsync(second.Entries[0].Destination,"unrelated");await Expect<IOException>(()=>service.CopyAsync(second,null,default));Check(File.ReadAllText(second.Entries[0].Destination)=="unrelated");
        });
        test("Recycle rejects shared source references and archived groups without calling the recycler",async ()=>
        {
            var root=Area();var photo=await PhotoAt(root);var group=Group(photo);var other=Group(photo.CopyReference());var recycler=new Recycler(Path.Combine(root,"recycle"));var service=new PhotoLifecycleService(new Store(),Path.Combine(root,"managed"),recycler);
            await Expect<IOException>(()=>service.PlanRecycleAsync(group,[photo],new() { Groups=[group,other] },default));group.Archived=true;await Expect<InvalidOperationException>(()=>service.PlanRecycleAsync(group,[photo],new(),default));Check(recycler.Calls==0 && File.Exists(photo.Path));
        });
        test("Recycle removes only successful references, records failed files, and can retry a pre-dispatch rejection",async ()=>
        {
            var root=Area();var a=await PhotoAt(root,"a.jpg");var b=await PhotoAt(root,"b.jpg");var group=Group(a,b);var recycler=new Recycler(Path.Combine(root,"recycle")) { FailName="b.jpg" };var service=new PhotoLifecycleService(new Store(),Path.Combine(root,"managed"),recycler);var operation=await service.PlanRecycleAsync(group,[a,b],new() { Groups=[group] },default);var removed=new HashSet<string>();
            await service.RecycleAsync(operation,e=> { foreach(var id in e.PhotoIds) removed.Add(id);return Task.CompletedTask; },null,default);
            Check(operation.Status=="Interrupted" && removed.SetEquals([a.Id]) && !File.Exists(a.Path) && File.Exists(b.Path) && operation.Entries[1].Stage=="Failed");
            recycler.FailName=null;await service.RecycleAsync(operation,e=> { foreach(var id in e.PhotoIds) removed.Add(id);return Task.CompletedTask; },null,default);Check(operation.Status=="Completed" && removed.SetEquals([a.Id,b.Id]) && recycler.Calls==3);
        });
        test("Unknown recycle outcome never deletes a replacement file at the old source path",async ()=>
        {
            var root=Area();var photo=await PhotoAt(root);var group=Group(photo);var recycler=new Recycler(Path.Combine(root,"recycle")) { Unknown=true };var service=new PhotoLifecycleService(new Store(),Path.Combine(root,"managed"),recycler);var operation=await service.PlanRecycleAsync(group,[photo],new() { Groups=[group] },default);var removed=0;
            await service.RecycleAsync(operation,_=> { removed++;return Task.CompletedTask; },null,default);await File.WriteAllTextAsync(photo.Path,"replacement must remain");
            await service.RecycleAsync(operation,_=> { removed++;return Task.CompletedTask; },null,default);Check(recycler.Calls==1 && removed==0 && File.ReadAllText(photo.Path)=="replacement must remain" && operation.Entries[0].Stage=="Recycling");
        });
        test("A changed file after recycle planning is preserved with its reference",async ()=>
        {
            var root=Area();var photo=await PhotoAt(root);var group=Group(photo);var recycler=new Recycler(Path.Combine(root,"recycle"));var service=new PhotoLifecycleService(new Store(),Path.Combine(root,"managed"),recycler);var operation=await service.PlanRecycleAsync(group,[photo],new() { Groups=[group] },default);await File.AppendAllTextAsync(photo.Path,"new content");var removed=0;
            await service.RecycleAsync(operation,_=> { removed++;return Task.CompletedTask; },null,default);Check(removed==0 && recycler.Calls==0 && File.Exists(photo.Path) && operation.Status=="Interrupted");
        });
        test("Recycle receipt survives a workspace-removal callback failure without deleting again",async ()=>
        {
            var root=Area();var photo=await PhotoAt(root);var group=Group(photo);var recycler=new Recycler(Path.Combine(root,"recycle"));var service=new PhotoLifecycleService(new Store(),Path.Combine(root,"managed"),recycler);var operation=await service.PlanRecycleAsync(group,[photo],new() { Groups=[group] },default);
            await service.RecycleAsync(operation,_=>throw new IOException("Synthetic workspace save interrupted"),null,default);Check(operation.Entries[0].Stage=="Recycled" && operation.Entries[0].RecycleIdentity.Length>0);
            var removed=0;await service.RecycleAsync(operation,_=> { removed++;return Task.CompletedTask; },null,default);Check(removed==1 && recycler.Calls==1 && operation.Status=="Completed");
        });
        test("Canceling the selected copy placeholder cancels both owner groups without blocking another group",async ()=>
        {
            var source=Group(new Photo());var target=Group(new Photo());var other=Group(new Photo());var coordinator=new GroupTaskCoordinator();var started=new TaskCompletionSource();var finished=0;
            var copy=coordinator.RunManyAsync([source,target],"Copy",true,async ct=> { started.SetResult();await Task.Delay(Timeout.Infinite,ct); },_=>{});
            await started.Task;await coordinator.RunAsync(other,"Recognition",false,_=> { finished++;return Task.CompletedTask; },()=>{});coordinator.Cancel(target.Id);await copy;
            Check(source.TaskState==GroupTaskState.Canceled && target.TaskState==GroupTaskState.Canceled && other.TaskState==GroupTaskState.Completed && finished==1);
        });
        test("Portable import carries owned photo bytes and journals, rebases owned paths only, and preserves external archive paths",async ()=>
        {
            var source=Area();var dataPhoto=await PhotoAt(Path.Combine(source,"managedphotos",Guid.NewGuid().ToString("N")),"backup-image.jpg");var outside=await PhotoAt(Area(),"external.jpg");var store=new StateStore(source);var state=new WorkspaceState { SchemaVersion=2,Groups=[Group(dataPhoto,outside)] };store.Save(state);
            var managedCopy=Path.Combine(source,"managedphotos",Guid.NewGuid().ToString("N"),"copy.jpg");store.SavePhotoOperation(new() { Id="copy",Kind="Copy",Status="Interrupted",Entries=[new() { Source=dataPhoto.Path,Destination=managedCopy,Temporary=managedCopy+".partial" }] });store.SaveJob(new() { Id="archive",Status="Completed",Entries=[new() { Source=dataPhoto.Path,Destination=outside.Path }] });
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();var originalDb=await ArchiveService.HashAsync(Path.Combine(source,"cararchive.db"));var destination=Path.Combine(Area(),"Data");await PortableData.ImportAsync(source,destination);var movedStore=new StateStore(destination);var loaded=movedStore.Load();var copied=loaded.Groups[0].Photos[0];
            Check(copied.Path.StartsWith(destination) && copied.OriginalPath==copied.Path && await ArchiveService.HashAsync(copied.Path)==await ArchiveService.HashAsync(dataPhoto.Path));Check(loaded.Groups[0].Photos[1].Path==outside.Path);
            var operation=movedStore.PhotoOperations().Single();Check(operation.Entries[0].Source.StartsWith(destination) && operation.Entries[0].Destination.StartsWith(destination) && operation.Entries[0].Temporary.StartsWith(destination));
            Check(movedStore.Jobs().Single().Entries[0].Source.StartsWith(destination) && movedStore.Jobs().Single().Entries[0].Destination==outside.Path && await ArchiveService.HashAsync(Path.Combine(source,"cararchive.db"))==originalDb);
        });
        test("Photo operation journal persists independently of archive logs and pre-upgrade backup is one-time",async ()=>
        {
            var root=Area();var store=new StateStore(root);store.Save(new WorkspaceState());store.SaveJob(new ArchiveJob { Id="history",Status="Completed" });store.BackupPhotoLifecycleOnce();var backup=Directory.GetFiles(root,"cararchive.pre-1.1.5-photos.db").Single();var stamp=File.GetLastWriteTimeUtc(backup);store.SavePhotoOperation(new() { Id="copy",Kind="Copy",Status="Interrupted",Entries=[new() { Source="source.jpg",Stage="Verified" }] });store.BackupPhotoLifecycleOnce();
            Check(store.PhotoOperations().Single().Entries.Single().Stage=="Verified" && store.Jobs().Single().Id=="history" && File.GetLastWriteTimeUtc(backup)==stamp);await Task.CompletedTask;
        });
    }
}
