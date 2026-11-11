using System.Net;
using System.Text.Json;
using CarArchive.Core;
using Microsoft.Data.Sqlite;

static class V2Tests
{
    static void Check(bool value,string message="V2 assertion failed") { if(!value) throw new Exception(message); }
    static string Area() { var path=Path.Combine(Path.GetTempPath(),"CarArchive-v2-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path; }
    static async Task Throws<T>(Func<Task> run) where T:Exception { try { await run(); }catch(T) { return; }throw new Exception("Expected "+typeof(T).Name); }
    static (WorkspaceState State,PhotoGroup A,PhotoGroup B) Shared(string source)
    {
        var asset=new ImageAsset { OriginalPath=source };var photo=new Photo { OriginalPath=source,Path=source,Crop=new(.1,.1,.4,.4) };photo.Attach(asset);
        var a=new PhotoGroup { Photos=[photo] };var b=new PhotoGroup { Photos=[photo.CopyReference(true)] };
        return (new() { SchemaVersion=2,Assets=[asset],Groups=[a,b] },a,b);
    }
    public static void Register(Action<string,Func<Task>> test)
    {
        test("V2: task snapshots preserve reference identity without attaching mutable asset events",()=> {
            var pair=Shared("photo.jpg");var photo=pair.A.Photos[0];var snapshot=photo.Snapshot();Check(snapshot.Id==photo.Id && snapshot.AssetId==photo.AssetId && snapshot.Asset is null && snapshot.Crop==photo.Crop);return Task.CompletedTask;
        });
        test("V2: merging duplicate references archives one original and reconciles both references",async ()=> {
            var area=Area();var source=Path.Combine(area,"photo.jpg");await File.WriteAllTextAsync(source,"shared");var pair=Shared(source);pair.A.Photos.Add(pair.B.Photos[0]);pair.State.Groups.Remove(pair.B);
            var archive=new ArchiveService(new StateStore(Path.Combine(area,"db")));var job=await archive.PlanAsync(pair.A,Path.Combine(area,"library"),"Brand","Car",default,pair.State);Check(job.Entries.Count==1);await archive.ExecuteAsync(job,default);ArchiveService.Reconcile(pair.State,[job]);
            Check(!File.Exists(source) && pair.A.Photos.All(p=>p.Path==job.Entries[0].Destination));
        });
        test("V2: shared references have independent crops and IDs",()=> {
            var pair=Shared("photo.jpg");Check(pair.A.Photos[0].Asset==pair.B.Photos[0].Asset);Check(pair.A.Photos[0].Id!=pair.B.Photos[0].Id);
            Check(pair.A.Photos[0].Crop is not null && pair.B.Photos[0].Crop is null);pair.B.Photos[0].Excluded=true;Check(!pair.A.Photos[0].Excluded);return Task.CompletedTask;
        });
        test("V2: shared original survives first archive, last archive removes it, undo preserves other copies",async ()=> {
            var area=Area();var source=Path.Combine(area,"shared.jpg");await File.WriteAllTextAsync(source,"original shared image bytes");var pair=Shared(source);
            var store=new StateStore(Path.Combine(area,"db"));store.Save(pair.State);var archive=new ArchiveService(store);
            var a=await archive.PlanAsync(pair.A,Path.Combine(area,"library"),"Brand","Car A",default,pair.State);Check(a.Entries[0].RetainSource);await archive.ExecuteAsync(a,default);pair.A.Archived=true;
            Check(File.Exists(source));var b=await archive.PlanAsync(pair.B,Path.Combine(area,"library"),"Brand","Car B",default,pair.State);Check(!b.Entries[0].RetainSource);await archive.ExecuteAsync(b,default);pair.B.Archived=true;
            Check(!File.Exists(source) && File.Exists(a.Entries[0].Destination) && File.Exists(b.Entries[0].Destination));
            await archive.UndoAsync(a,default);Check(File.Exists(source) && !File.Exists(a.Entries[0].Destination) && File.Exists(b.Entries[0].Destination));
            await archive.UndoAsync(b,default);Check(await File.ReadAllTextAsync(source)=="original shared image bytes" && !File.Exists(b.Entries[0].Destination));
        });
        test("V2: undo before the other shared group archives reuses original safely",async ()=> {
            var area=Area();var source=Path.Combine(area,"shared.jpg");await File.WriteAllTextAsync(source,"shared");var pair=Shared(source);var archive=new ArchiveService(new StateStore(Path.Combine(area,"db")));
            var job=await archive.PlanAsync(pair.B,Path.Combine(area,"library"),"Brand","Car B",default,pair.State);await archive.ExecuteAsync(job,default);Check(File.Exists(source));
            await archive.UndoAsync(job,default);Check(await File.ReadAllTextAsync(source)=="shared" && !File.Exists(job.Entries[0].Destination));
        });
        test("V2: shared undo refuses a changed original without removing copies",async ()=> {
            var area=Area();var source=Path.Combine(area,"shared.jpg");await File.WriteAllTextAsync(source,"shared");var pair=Shared(source);var archive=new ArchiveService(new StateStore(Path.Combine(area,"db")));
            var job=await archive.PlanAsync(pair.A,Path.Combine(area,"library"),"Brand","Car A",default,pair.State);await archive.ExecuteAsync(job,default);await File.WriteAllTextAsync(source,"user modification");
            await Throws<IOException>(()=>archive.UndoAsync(job,default));Check(File.Exists(job.Entries[0].Destination) && await File.ReadAllTextAsync(source)=="user modification");
        });
        test("V2: legacy database migrates with backup, stable assets, naming and credential file preserved",async ()=> {
            var area=Area();var source=Path.Combine(area,"photo.jpg");await File.WriteAllTextAsync(source,"bytes");var pair=Shared(source);pair.State.SchemaVersion=1;pair.State.Assets=[];
            foreach(var p in pair.State.Groups.SelectMany(g=>g.Photos)) p.AssetId="";
            pair.State.Settings.Mode=RecognitionMode.Accurate;pair.State.Names.Add(new("identity","Brand","Remembered Car"));
            var key=Path.Combine(area,"gemini-key.bin");await File.WriteAllTextAsync(key,"encrypted marker");
            using(var db=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=Path.Combine(area,"cararchive.db") }.ToString()))
            { db.Open();using var cmd=db.CreateCommand();cmd.CommandText="CREATE TABLE workspace(id INTEGER PRIMARY KEY,json TEXT NOT NULL); INSERT INTO workspace VALUES(1,$json);";cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(pair.State,StateStore.Json));cmd.ExecuteNonQuery(); }
            var store=new StateStore(area);var state=store.Load();Check(state.SchemaVersion==2 && state.Settings.Mode==RecognitionMode.FieldsOnly);Check(state.Assets.Count==1 && state.Groups.Count==2);
            Check(state.Groups[0].Photos[0].AssetId==state.Groups[1].Photos[0].AssetId && state.Names.Single().FolderName=="Remembered Car");
            Check(Directory.GetFiles(area,"*.pre-v2-*.db").Length==1 && await File.ReadAllTextAsync(key)=="encrypted marker");
            var again=store.Load();Check(again.Assets[0].Id==state.Assets[0].Id && Directory.GetFiles(area,"*.pre-v2-*.db").Length==1);
        });
        test("V2: incremental save writes one group without flushing another group's unsaved edits",()=> {
            var area=Area();var store=new StateStore(area);var a=new PhotoGroup { FolderName="A" };var b=new PhotoGroup { FolderName="B" };var state=new WorkspaceState { SchemaVersion=2,Groups=[a,b] };store.Save(state);
            a.FolderName="A changed";b.FolderName="B unsaved";store.SaveGroup(a);var loaded=store.Load();Check(loaded.Groups[0].FolderName=="A changed" && loaded.Groups[1].FolderName=="B");return Task.CompletedTask;
        });
        test("V2: two queries plus file work can run while a fourth group is edited; completion is isolated",async ()=> {
            var queue=new GroupTaskCoordinator();var a=new PhotoGroup();var b=new PhotoGroup();var c=new PhotoGroup();var d=new PhotoGroup();
            var finishA=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var finishB=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var finishC=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var taskA=queue.RunAsync(a,"A",false,async ct=> { await finishA.Task.WaitAsync(ct);a.FolderName="A result"; },()=>{});
            var taskB=queue.RunAsync(b,"B",false,async ct=> { await finishB.Task.WaitAsync(ct);b.FolderName="B result"; },()=>{});
            var taskC=queue.RunAsync(c,"C",true,ct=>finishC.Task.WaitAsync(ct),()=>{});
            Check(a.TaskState==GroupTaskState.Running && b.TaskState==GroupTaskState.Running && c.TaskState==GroupTaskState.Running);d.FolderName="D manual edit";
            finishB.SetResult();await taskB;Check(b.TaskState==GroupTaskState.Completed && a.IsWorking && d.FolderName=="D manual edit");
            finishA.SetResult();finishC.SetResult();await Task.WhenAll(taskA,taskC);Check(a.FolderName=="A result" && b.FolderName=="B result" && d.FolderName=="D manual edit");
        });
        test("V2: queued third query cancellation does not cancel running groups",async ()=> {
            var queue=new GroupTaskCoordinator();var a=new PhotoGroup();var b=new PhotoGroup();var c=new PhotoGroup();var finish=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ta=queue.RunAsync(a,"A",false,ct=>finish.Task.WaitAsync(ct),()=>{});var tb=queue.RunAsync(b,"B",false,ct=>finish.Task.WaitAsync(ct),()=>{});
            var entered=false;var tc=queue.RunAsync(c,"C",false,_=> { entered=true;return Task.CompletedTask; },()=>{});Check(c.TaskState==GroupTaskState.Queued);queue.Cancel(c.Id);await tc;
            Check(c.TaskState==GroupTaskState.Canceled && !entered && a.IsWorking && b.IsWorking);finish.SetResult();await Task.WhenAll(ta,tb);
        });
        test("V2: a group cannot start two tasks concurrently",async ()=> {
            var queue=new GroupTaskCoordinator();var a=new PhotoGroup();var finish=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var running=queue.RunAsync(a,"A",false,ct=>finish.Task.WaitAsync(ct),()=>{});
            await Throws<InvalidOperationException>(()=>queue.RunAsync(a,"duplicate",true,_=>Task.CompletedTask,()=>{}));finish.SetResult();await running;
        });
        test("V2: fields-only sends one image, nine fields, low thinking and a 4096 token budget",async ()=> {
            var calls=0;using var http=new HttpClient(new Handler(request=> {
                calls++;using var body=JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());var root=body.RootElement;
                Check(root.GetProperty("input").EnumerateArray().Count(e=>e.GetProperty("type").GetString()=="image")==1);
                var schema=root.GetProperty("response_format").GetProperty("schema");Check(schema.GetProperty("properties").EnumerateObject().Count()==9 && schema.GetProperty("properties").TryGetProperty("officialName",out _) && schema.GetProperty("properties").TryGetProperty("body",out _) && !schema.GetProperty("properties").TryGetProperty("research",out _));
                Check(root.GetProperty("generation_config").GetProperty("max_output_tokens").GetInt32()==4096 && root.GetProperty("generation_config").GetProperty("thinking_level").GetString()=="low");
                return Response("{\"brand\":\"Audi\",\"series\":\"A4\",\"code\":\"B9\"}");
            }));
            var service=new GeminiService(http,new Images(),()=>"test-key",()=>{});var result=await service.RecognizeAsync([new(),new(),new()],new() { Mode=RecognitionMode.FieldsOnly },true,default);Check(calls==1 && result.Identity.Code=="B9" && result.Research.Length==0);
        });
        test("V2: unsupported image-search preference fails before any paid request",async ()=> {
            var called=false;using var http=new HttpClient(new Handler(_=> { called=true;return Response("{}"); }));var service=new GeminiService(http,new Images(),()=>"test-key",()=>{});
            await Throws<InvalidOperationException>(()=>service.RecognizeAsync([new Photo()],new() { Search=SearchPreference.ImageReference },false,default));Check(!called);
        });
        test("V2: model-output parsing excludes thought summaries and reports raw search count",()=> {
            var response=new { steps=new object[] { new { type="thought",summary=new[] { new { type="text",text="This is not JSON" } } },new { type="google_search_call",queries=new[] { "q","q" },search_type="image_search" },new { type="model_output",content=new[] { new { type="text",text="{\"brand\":\"Audi\"}" } } } },usage=new { total_input_tokens=12,total_output_tokens=34,total_thought_tokens=20 } };
            using var doc=JsonDocument.Parse(JsonSerializer.Serialize(response));var parsed=GeminiService.ParseRecognition(doc.RootElement,"test");Check(parsed.Identity.Brand=="Audi" && parsed.SearchCount==2 && parsed.ThinkingTokens==20 && parsed.ActualSearch.Contains("图片资料"));return Task.CompletedTask;
        });
        test("V2: incomplete output is rejected without accepting a partial classification",async ()=> {
            using var doc=JsonDocument.Parse("{\"status\":\"incomplete\",\"steps\":[]}");await Throws<InvalidDataException>(()=>Task.Run(()=>GeminiService.ParseRecognition(doc.RootElement,"test")));
        });
    }
    static HttpResponseMessage Response(string text)=>new(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(new { steps=new[] { new { type="model_output",content=new[] { new { type="text",text } } } } })) };
    sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(respond(request)); }
    sealed class Images:IPhotoService { public Task<Photo> ImportAsync(string path,CancellationToken ct)=>Task.FromResult(new Photo { Path=path });public Task<byte[]> AnalysisJpegAsync(Photo photo,int longestEdge,CancellationToken ct)=>Task.FromResult(new byte[]{1,2,3}); }
}
