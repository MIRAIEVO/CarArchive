using System.Net;
using System.Text;
using System.Text.Json;
using CarArchive.Core;

var tests = new List<(string Name,Func<Task> Test)>();
void Test(string name,Func<Task> run) => tests.Add((name,run));
void Assert(bool condition,string message="Assertion failed") { if(!condition) throw new Exception(message); }
async Task Throws<T>(Func<Task> run) where T:Exception { try { await run(); } catch(T) { return; } throw new Exception("Expected "+typeof(T).Name); }
var temp=Path.Combine(Path.GetTempPath(),"CarArchive-tests-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
string Area() { var p=Path.Combine(temp,Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
PhotoGroup Group(string path) => new() { Photos=[new() { Path=path,OriginalPath=path }],GroupConfirmed=true };
async Task<string> Source(string area,string name="photo.jpg",string content="original photo bytes") { var p=Path.Combine(area,name); await File.WriteAllTextAsync(p,content); return p; }

Test("Default names preserve body, chassis and facelift; unknown trim falls back to series",()=> {
    Assert(NamingService.DefaultName(new() { Brand="Audi",Series="A4",Body="Avant",Package="S-line",Code="B9",Phase="Facelift" })=="A4 Avant S-line (B9 Facelift)");
    Assert(NamingService.DefaultName(new() { Series="S-Class",Code="W223" })=="S-Class (W223)");
    Assert(NamingService.DefaultName(new() { Series="Swift",Generation="5th",Phase="Pre-Facelift" })=="Swift (5th)"); return Task.CompletedTask;
});
Test("Category catalog merges disk and remembered endpoints using brand mappings without writing files",async ()=> {
    var root=Area();var disk=Path.Combine(root,"Benz","S-Class (W223)");Directory.CreateDirectory(disk);
    var source=await Source(disk);var hash=await ArchiveService.HashAsync(source);
    var directories=Directory.GetDirectories(root,"*",SearchOption.AllDirectories).Order().ToArray();
    var catalog=CategoryCatalogService.Scan(root,[new("1","Mercedes-Benz","s-class (w223)"),new("2","Mercedes-Benz","E-Class (W214)"),new("3","Audi","S-Class (W223)")],new Dictionary<string,string> { ["Mercedes-Benz"]="Benz" });
    Assert(catalog.Items.Count==3 && catalog.Warnings.Count==0);
    var existing=catalog.Items.Single(item=>item.Exists);Assert(existing.BrandDirectory=="Benz" && existing.FolderName=="S-Class (W223)" && existing.TargetPath==disk);
    Assert(catalog.Items.Any(item=>item.BrandDirectory=="Audi") && catalog.Items.Count(item=>!item.Exists)==2);
    Assert(directories.SequenceEqual(Directory.GetDirectories(root,"*",SearchOption.AllDirectories).Order()) && await ArchiveService.HashAsync(source)==hash);
});
Test("Category suggestions match multiple keywords, case and whitespace, retaining distinct brand targets",()=> {
    var items=new[] { new CategorySuggestion("Audi","A4 Avant (B9 Facelift)","a",true),new("Other","A4 Avant (B9 Facelift)","b",true),new("Benz","S-Class (W223)","c",false) };
    Assert(CategoryCatalogService.Match(items,"  a4   b9 FACELIFT ","Audi").Count==2);
    Assert(CategoryCatalogService.Match(items,"benz w223","").Single().TargetPath=="c");
    Assert(CategoryCatalogService.Match(items,"absent","").Count==0 && CategoryCatalogService.Match(items,"  ","").Count==0);
    Assert(CategoryCatalogService.Match(items,"b9","").Count==2);return Task.CompletedTask;
});
Test("Category suggestions rank exact then prefix then substring, prefer current brand within rank, and cap at twenty",()=> {
    var items=new List<CategorySuggestion> { new("Audi","A4","1",true),new("Benz","A4","2",true),new("Benz","A4 Avant","3",true),new("Benz","Old A4","4",true) };
    var sorted=CategoryCatalogService.Match(items,"a4","Benz");Assert(sorted.Select(x=>x.TargetPath).SequenceEqual(new[] { "2","1","3","4" }));
    items.AddRange(Enumerable.Range(0,50).Select(i=>new CategorySuggestion("Audi","A4 "+i,"extra"+i,true)));
    Assert(CategoryCatalogService.Match(items,"a4","Audi",100).Count==20);return Task.CompletedTask;
});
Test("Category catalog switches roots without reusing old endpoints and tolerates missing roots and incomplete memories",()=> {
    var first=Area();var second=Area();Directory.CreateDirectory(Path.Combine(first,"Audi","A4"));Directory.CreateDirectory(Path.Combine(second,"Benz","S-Class"));
    Assert(CategoryCatalogService.Scan(second,[],new Dictionary<string,string>()).Items.Single().FolderName=="S-Class");
    var missing=Path.Combine(second,"not-created");
    var catalog=CategoryCatalogService.Scan(missing,[new("1","Audi","A4"),new("2","","unknown"),new("3","Audi","..\\escape")],new Dictionary<string,string>());
    Assert(catalog.Items.Count==1 && !catalog.Items[0].Exists && !Directory.Exists(missing));
    Assert(CategoryCatalogService.Scan("",[new("1","Audi","A4")],new Dictionary<string,string>()).Items.Single().TargetPath=="");return Task.CompletedTask;
});
Test("Applying an endpoint changes only selected group archive fields and never creates a directory",()=> {
    var root=Area();var target=Path.Combine(root,"Benz","S-Class (W223)");
    var identity=new CarIdentity { Brand="Other",Series="Unknown" };var group=new PhotoGroup { Confirmed=identity,FolderName="before",TargetBrand="Other" };var other=new PhotoGroup { FolderName="untouched" };
    CategoryCatalogService.Apply(group,new("Benz","S-Class (W223)",target,false));
    Assert(group.FolderName=="S-Class (W223)" && group.TargetBrand=="Benz" && group.ManualName && group.PreviousFolderName=="before");
    Assert(ReferenceEquals(identity,group.Confirmed) && identity.Brand=="Other" && other.FolderName=="untouched" && !Directory.Exists(target));return Task.CompletedTask;
});
Test("Category selection rejects working, archived, and invalid targets without partial changes",async ()=> {
    foreach(var group in new[] { new PhotoGroup { Archived=true },new PhotoGroup { TaskState=GroupTaskState.Running },new PhotoGroup { TaskState=GroupTaskState.Queued } })
        await Throws<InvalidOperationException>(()=>Task.Run(()=>CategoryCatalogService.Apply(group,new("Audi","A4","",true))));
    var editable=new PhotoGroup { FolderName="original",TargetBrand="Audi" };
    await Throws<ArgumentException>(()=>Task.Run(()=>CategoryCatalogService.Apply(editable,new("..","A4","",false))));
    Assert(editable.FolderName=="original" && editable.TargetBrand=="Audi");
});
Test("Category scan supports cancellation and refreshed remembered names",async ()=> {
    using var cancellation=new CancellationTokenSource();cancellation.Cancel();
    await Throws<OperationCanceledException>(()=>Task.Run(()=>CategoryCatalogService.Scan("",[new("1","Audi","A4")],new Dictionary<string,string>(),cancellation.Token)));
    var names=new List<LearnedName> { new("1","Audi","Old name") };
    names[0]=names[0] with { FolderName="New name" };var catalog=CategoryCatalogService.Scan("",names,new Dictionary<string,string>());
    Assert(CategoryCatalogService.Match(catalog.Items,"old","").Count==0 && CategoryCatalogService.Match(catalog.Items,"new","").Count==1);
});
Test("Learned exact names precede vocabulary rules and stay isolated by generation",()=> {
    var naming=new NamingService(); var state=new WorkspaceState(); var identity=new CarIdentity { Brand="Mercedes-Benz",Series="E-Class",Body="Estate",Code="S212" };
    state.Rules.Add(new() { Brand="Mercedes-Benz",From="Estate",To="Station Wagon" });
    Assert(naming.Suggest(identity,state,[]).Name=="E-Class Station Wagon (S212)");
    naming.Remember(identity,"Benz E-Class Estate AMG-Line (S212)",state);
    Assert(naming.Suggest(identity,state,[]).Name=="Benz E-Class Estate AMG-Line (S212)"); identity.Code="S214";
    Assert(naming.Suggest(identity,state,[]).Name=="E-Class Station Wagon (S214)"); state.Rules[0].Enabled=false;
    Assert(naming.Suggest(identity,state,[]).Name=="E-Class Estate (S214)"); return Task.CompletedTask;
});
Test("Learning only generalizes explicit vocabulary substitutions",()=> {
    var naming=new NamingService(); Assert(naming.ProposeRule("E-Class Estate (S212)","E-Class Station Wagon (S212)","Benz")?.From=="Estate");
    Assert(naming.ProposeRule("S 500 (W223)","S 580 (W223)","Benz") is null); return Task.CompletedTask;
});
Test("Brand rules override global rules without recursive substitution",()=> {
    var state=new WorkspaceState(); state.Rules.Add(new() { From="Estate",To="Wagon" });
    state.Rules.Add(new() { Brand="Benz",From="Estate",To="Station Wagon" }); state.Rules.Add(new() { From="Station Wagon",To="Estate" });
    var naming=new NamingService(); Assert(naming.Suggest(new() { Brand="Benz",Series="E-Class",Body="Estate" },state,[]).Name=="E-Class Station Wagon"); return Task.CompletedTask;
});
Test("Windows invalid directory segments are rejected",async ()=> {
    foreach(var segment in new[] { "..","CON","NUL.txt","bad/name","bad\\name","bad.","" }) await Throws<ArgumentException>(()=>Task.Run(()=>NamingService.SafeSegment(segment)));
});
Test("Missing EXIF stays separate; timestamp gaps split groups",()=> {
    var time=DateTimeOffset.Now;
    var groups=GroupingService.Suggest(new[] { new Photo { Path="1.jpg",CapturedAt=time },new Photo { Path="2.jpg",CapturedAt=time.AddSeconds(10) },new Photo { Path="3.jpg",CapturedAt=time.AddMinutes(5) },new Photo { Path="4.jpg" } },90);
    Assert(groups.Count==3 && groups[0].Photos.Count==2); return Task.CompletedTask;
});
Test("Visual grouping cannot invent, duplicate or lose photos",()=> {
    var photos=new List<Photo> { new() { Path="1.jpg" },new() { Path="2.jpg" },new() { Path="3.jpg" } };
    var groups=GroupingService.FromVisual(photos,[[photos[0].Id,"invented",photos[0].Id],[photos[1].Id]]);
    Assert(groups.SelectMany(g=>g.Photos).Select(p=>p.Id).Distinct().Count()==3); Assert(groups.Sum(g=>g.Photos.Count)==3); return Task.CompletedTask;
});
Test("Mixed EXIF and missing dates produce a transitive stable ordering",()=> {
    var time=DateTimeOffset.Now; var photos=new[] { new Photo { Path="IMG_1.jpg",CapturedAt=time.AddDays(1) },new Photo { Path="IMG_3.jpg",CapturedAt=time },new Photo { Path="IMG_2.jpg" },new Photo { Path="IMG_10.jpg" } };
    var sorted=GroupingService.Sort(photos); Assert(sorted[0].Name=="IMG_3.jpg" && sorted[1].Name=="IMG_1.jpg" && sorted[2].Name=="IMG_2.jpg"); return Task.CompletedTask;
});
Test("SQLite persists suggestions separately from edited confirmation",()=> {
    var store=new StateStore(Area()); var state=new WorkspaceState { Groups=[new() { Suggestion=new() { Identity=new() { Series="S-Class" } },Confirmed=new() { Series="S 580" } }] };
    store.Save(state); var read=store.Load(); Assert(read.Groups[0].Suggestion!.Identity.Series=="S-Class" && read.Groups[0].Confirmed.Series=="S 580"); return Task.CompletedTask;
});
Test("Removing multiple imported and archived groups preserves their source photos and assets",async ()=> {
    var area=Area(); var sources=new[] { await Source(area,"first.jpg"),await Source(area,"second.jpg") };
    var assets=sources.Select(source=>new ImageAsset { OriginalPath=source }).ToList();
    var groups=assets.Select((asset,index)=> {
        var photo=new Photo { AssetId=asset.Id,Path=asset.OriginalPath,OriginalPath=asset.OriginalPath }; photo.Attach(asset);
        return new PhotoGroup { Photos=[photo],ImportBatchId=Guid.NewGuid().ToString("N"),Archived=index==1 };
    }).ToList();
    var store=new StateStore(Path.Combine(area,"db")); var state=new WorkspaceState { Assets=assets,Groups=groups };
    store.Save(state); state.Groups.Clear(); store.Save(state);
    var read=store.Load();
    Assert(sources.All(File.Exists) && read.Groups.Count==0 && assets.All(asset=>read.Assets.Any(item=>item.Id==asset.Id)));
});
Test("Archive and undo preserve all original bytes",async ()=> {
    var area=Area(); var source=await Source(area); var hash=await ArchiveService.HashAsync(source); var store=new StateStore(Path.Combine(area,"db")); var service=new ArchiveService(store);
    var job=await service.PlanAsync(Group(source),Path.Combine(area,"library"),"Audi","A4 (B9)",default); await service.ExecuteAsync(job,default);
    Assert(!File.Exists(source) && await ArchiveService.HashAsync(job.Entries[0].Destination)==hash); Assert(store.Jobs().Single().Status=="Completed");
    await service.UndoAsync(job,default); Assert(await ArchiveService.HashAsync(source)==hash && !File.Exists(job.Entries[0].Destination));
});
Test("Cross-volume archive and undo preserve SHA-256",async ()=> {
    var area=Area(); var source=await Source(area); var destinationRoot=Path.Combine(Environment.CurrentDirectory,"verification-output","cross-volume-"+Guid.NewGuid().ToString("N"));
    if(string.Equals(Path.GetPathRoot(source),Path.GetPathRoot(destinationRoot),StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("  Cross-volume environment unavailable; ordinary copy/verify/move covered separately."); return; }
    var hash=await ArchiveService.HashAsync(source); var service=new ArchiveService(new StateStore(Path.Combine(area,"db")));
    var job=await service.PlanAsync(Group(source),destinationRoot,"Audi","A4",default); await service.ExecuteAsync(job,default);
    Assert(await ArchiveService.HashAsync(job.Entries[0].Destination)==hash && !File.Exists(source)); await service.UndoAsync(job,default); Assert(await ArchiveService.HashAsync(source)==hash);
});
Test("Partially completed multi-photo jobs can be safely undone",async ()=> {
    var area=Area(); var first=await Source(area,"first.jpg"); var second=await Source(area,"second.jpg","second contents"); var store=new StateStore(Path.Combine(area,"db")); var service=new ArchiveService(store);
    var group=Group(first); group.Photos.Add(new() { Path=second,OriginalPath=second }); var job=await service.PlanAsync(group,Path.Combine(area,"library"),"Audi","A4",default);
    Directory.CreateDirectory(Path.GetDirectoryName(job.Entries[1].Destination)!); await File.WriteAllTextAsync(job.Entries[1].Destination,"racing file");
    await Throws<IOException>(()=>service.ExecuteAsync(job,default)); Assert(!File.Exists(first) && File.Exists(second)); await service.UndoAsync(store.Jobs().First(j=>j.Id==job.Id),default);
    Assert(File.Exists(first) && File.Exists(second) && await File.ReadAllTextAsync(job.Entries[1].Destination)=="racing file");
});
Test("Same filename with different contents receives a suffix",async ()=> {
    var area=Area(); var source=await Source(area); var directory=Path.Combine(area,"library","Audi","A4"); Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory,"photo.jpg"),"other");
    var service=new ArchiveService(new StateStore(Path.Combine(area,"db"))); var job=await service.PlanAsync(Group(source),Path.Combine(area,"library"),"Audi","A4",default);
    Assert(job.Entries[0].Destination.EndsWith("photo (2).jpg")); await service.ExecuteAsync(job,default); Assert(await File.ReadAllTextAsync(Path.Combine(directory,"photo.jpg"))=="other");
});
Test("Identical contents at destination reject the group before moving",async ()=> {
    var area=Area(); var source=await Source(area); var directory=Path.Combine(area,"library","Audi","A4"); Directory.CreateDirectory(directory); File.Copy(source,Path.Combine(directory,"photo.jpg"));
    var service=new ArchiveService(new StateStore(Path.Combine(area,"db"))); await Throws<IOException>(()=>service.PlanAsync(Group(source),Path.Combine(area,"library"),"Audi","A4",default)); Assert(File.Exists(source));
});
Test("Source modified after planning is retained",async ()=> {
    var area=Area(); var source=await Source(area); var store=new StateStore(Path.Combine(area,"db")); var service=new ArchiveService(store);
    var job=await service.PlanAsync(Group(source),Path.Combine(area,"library"),"Audi","A4",default); await File.WriteAllTextAsync(source,"modified");
    await Throws<IOException>(()=>service.ExecuteAsync(job,default)); Assert(File.Exists(source) && !File.Exists(job.Entries[0].Destination) && store.Jobs().Single().Status=="Interrupted");
});
Test("Locked source produces a recoverable interruption",async ()=> {
    var area=Area(); var source=await Source(area); var store=new StateStore(Path.Combine(area,"db")); var service=new ArchiveService(store);
    var job=await service.PlanAsync(Group(source),Path.Combine(area,"library"),"Audi","A4",default);
    using(var locked=new FileStream(source,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) await Throws<IOException>(()=>service.ExecuteAsync(job,default));
    Assert(File.Exists(source)); await service.ExecuteAsync(store.Jobs().Single(),default); Assert(!File.Exists(source));
});
Test("Recovery handles a partial copy without deleting the source early",async ()=> {
    var area=Area(); var source=await Source(area); var store=new StateStore(Path.Combine(area,"db")); var service=new ArchiveService(store);
    var job=await service.PlanAsync(Group(source),Path.Combine(area,"library"),"Audi","A4",default); var e=job.Entries[0]; Directory.CreateDirectory(Path.GetDirectoryName(e.Destination)!); await File.WriteAllTextAsync(e.Temp,"partial"); e.Stage="Copying"; store.SaveJob(job);
    await service.ExecuteAsync(store.Jobs().Single(),default); Assert(!File.Exists(source) && await File.ReadAllTextAsync(e.Destination)=="original photo bytes");
});
Test("Recovery handles a crash after publication but before source deletion",async ()=> {
    var area=Area(); var source=await Source(area); var store=new StateStore(Path.Combine(area,"db")); var service=new ArchiveService(store);
    var job=await service.PlanAsync(Group(source),Path.Combine(area,"library"),"Audi","A4",default); var e=job.Entries[0]; Directory.CreateDirectory(Path.GetDirectoryName(e.Destination)!); File.Copy(source,e.Destination); e.Stage="Verified"; store.SaveJob(job);
    await service.ExecuteAsync(store.Jobs().Single(),default); Assert(!File.Exists(source) && File.Exists(e.Destination));
});
Test("Tampered destination never causes source deletion during recovery",async ()=> {
    var area=Area(); var source=await Source(area); var store=new StateStore(Path.Combine(area,"db")); var service=new ArchiveService(store);
    var job=await service.PlanAsync(Group(source),Path.Combine(area,"library"),"Audi","A4",default); var e=job.Entries[0]; Directory.CreateDirectory(Path.GetDirectoryName(e.Destination)!); await File.WriteAllTextAsync(e.Destination,"tampered"); e.Stage="Published"; store.SaveJob(job);
    await Throws<IOException>(()=>service.ExecuteAsync(store.Jobs().Single(),default)); Assert(File.Exists(source));
});
Test("Undo refuses to overwrite a re-created source",async ()=> {
    var area=Area(); var source=await Source(area); var service=new ArchiveService(new StateStore(Path.Combine(area,"db"))); var job=await service.PlanAsync(Group(source),Path.Combine(area,"library"),"Audi","A4",default); await service.ExecuteAsync(job,default);
    await File.WriteAllTextAsync(source,"new file"); await Throws<IOException>(()=>service.UndoAsync(job,default)); Assert(await File.ReadAllTextAsync(source)=="new file" && File.Exists(job.Entries[0].Destination));
});
Test("Cancellation retains source and leaves a resumable journal",async ()=> {
    var area=Area(); var source=await Source(area); var store=new StateStore(Path.Combine(area,"db")); var service=new ArchiveService(store); var job=await service.PlanAsync(Group(source),Path.Combine(area,"library"),"Audi","A4",default);
    using var cts=new CancellationTokenSource(); cts.Cancel(); await Throws<OperationCanceledException>(()=>service.ExecuteAsync(job,cts.Token)); Assert(File.Exists(source) && store.Jobs().Single().Status=="Pending");
    await service.ExecuteAsync(store.Jobs().Single(),default); Assert(!File.Exists(source));
});
Test("Forged model citations do not count as web verification",()=> {
    var content=JsonSerializer.Serialize(new RecognitionResult { Identity=new() { Brand="Audi" },Queries=["fabricated"],Sources=[new("fake","https://example.com","fake")] });
    using var doc=JsonDocument.Parse(JsonSerializer.Serialize(new { steps=new[] { new { type="model_output",content=new[] { new { type="text",text=content } } } } }));
    var result=GeminiService.ParseRecognition(doc.RootElement,"test"); Assert(!result.Grounded && result.Queries.Count==0 && result.Sources.Count==0); return Task.CompletedTask;
});
Test("Real tool queries plus annotated sources count as grounded",()=> {
    var text=JsonSerializer.Serialize(new RecognitionResult { Identity=new() { Brand="Audi",Code="B9" },Research="verified" });
    var body=new { steps=new object[] { new { type="google_search_call",arguments=new { queries=new[] { "Audi B9 facelift" } } },new { type="model_output",content=new[] { new { type="text",text,annotations=new[] { new { type="url_citation",url="https://www.audi-mediacenter.com/",title="Audi",start_index=0,end_index=5 } } } } } },usage=new { total_input_tokens=123,total_output_tokens=45 } };
    using var doc=JsonDocument.Parse(JsonSerializer.Serialize(body)); var result=GeminiService.ParseRecognition(doc.RootElement,"test"); Assert(result.Grounded && result.Sources.Count==1 && result.InputTokens==123); return Task.CompletedTask;
});
Test("Conflict remains unverified even with citations",()=> {
    var text=JsonSerializer.Serialize(new RecognitionResult { Conflict=true });
    var body=new { steps=new object[] { new { type="google_search_call",arguments=new { queries=new[] { "query" } } },new { type="model_output",content=new[] { new { type="text",text,annotations=new[] { new { type="url_citation",url="https://example.com",start_index=0,end_index=1 } } } } } } };
    using var doc=JsonDocument.Parse(JsonSerializer.Serialize(body)); Assert(!GeminiService.ParseRecognition(doc.RootElement,"test").Grounded); return Task.CompletedTask;
});
Test("Body style is kept only when present in the official complete model name",()=> {
    RecognitionResult Parse(string body,string officialName) {
        var text=JsonSerializer.Serialize(new RecognitionResult { Identity=new() { Brand="Audi",Series="A4",Body=body,OfficialName=officialName } });
        using var doc=JsonDocument.Parse(JsonSerializer.Serialize(new { steps=new[] { new { type="model_output",content=new[] { new { type="text",text } } } } }));
        return GeminiService.ParseRecognition(doc.RootElement,"test");
    }
    Assert(Parse("Avant","Audi A4 Avant").Identity.Body=="Avant");
    Assert(Parse("SUV","Audi Q5").Identity.Body=="");
    Assert(Parse("van","Advanced").Identity.Body=="");
    Assert(Parse("Estate","").Identity.Body=="");
    return Task.CompletedTask;
});
Test("HTTP rate limits and permission errors do not retry or move anything",async ()=> {
    foreach(var status in new[] { HttpStatusCode.TooManyRequests,HttpStatusCode.Forbidden,HttpStatusCode.NotFound })
    { var calls=0; using var client=new HttpClient(new FakeHandler(_=>new(status) { Content=new StringContent("sensitive raw error") })); var service=new GeminiService(client,new FakeImages(),()=>"test-key",()=>calls++);
      await Throws<IOException>(()=>service.RecognizeAsync([new Photo()],new(),false,default)); Assert(calls==1); }
});
Test("Network failures and malformed responses surface without an automatic retry",async ()=> {
    var count=0; using(var client=new HttpClient(new FakeHandler(_=> { count++; throw new HttpRequestException("network failure"); })))
    { var service=new GeminiService(client,new FakeImages(),()=>"test-key",()=>{}); await Throws<IOException>(()=>service.RecognizeAsync([new Photo()],new(),false,default)); Assert(count==1); }
    using(var client=new HttpClient(new FakeHandler(_=>new(HttpStatusCode.OK) { Content=new StringContent("not json") })))
    { var service=new GeminiService(client,new FakeImages(),()=>"test-key",()=>{}); await Throws<InvalidDataException>(()=>service.RecognizeAsync([new Photo()],new(),false,default)); }
});
Test("Requests include real search tool, image data, revision and no stored interaction",async ()=> {
    var seen=false; using var client=new HttpClient(new FakeHandler(request=> {
        var body=request.Content!.ReadAsStringAsync().GetAwaiter().GetResult(); using var json=JsonDocument.Parse(body);
        Assert(request.Headers.Contains("Api-Revision") && request.Headers.Contains("x-goog-api-key")); Assert(!json.RootElement.GetProperty("store").GetBoolean());
        Assert(json.RootElement.GetProperty("tools")[0].GetProperty("type").GetString()=="google_search"); Assert(body.Contains("image/jpeg")); seen=true;
        return new(HttpStatusCode.OK) { Content=new StringContent("{\"steps\":[{\"type\":\"model_output\",\"content\":[{\"type\":\"text\",\"text\":\"{\\\"identity\\\":{\\\"brand\\\":\\\"Audi\\\"}}\"}]}]}") };
    }));
    var service=new GeminiService(client,new FakeImages(),()=>"test-key",()=>{}); var result=await service.RecognizeAsync([new Photo()],new(),false,default); Assert(seen && !result.Grounded);
});
V2Tests.Register(Test);
var failures=0; var started=DateTimeOffset.Now;
foreach(var test in tests) { try { await test.Test(); Console.WriteLine("PASS "+test.Name); } catch(Exception e) { failures++; Console.WriteLine("FAIL "+test.Name+"\n"+e); } }
Console.WriteLine($"{tests.Count-failures}/{tests.Count} passed in {(DateTimeOffset.Now-started).TotalSeconds:F2}s. Fixtures: {temp}");
Environment.ExitCode=failures==0?0:1;

sealed class FakeHandler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) => Task.FromResult(respond(request)); }
sealed class FakeImages:IPhotoService
{
    public Task<Photo> ImportAsync(string path,CancellationToken ct) => Task.FromResult(new Photo { Path=path });
    public Task<byte[]> AnalysisJpegAsync(Photo photo,int longestEdge,CancellationToken ct) => Task.FromResult(new byte[] { 1,2,3 });
}
