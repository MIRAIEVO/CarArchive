using System.Text.Json;
using CarArchive.Core;

static class UsabilityTests
{
    static void Check(bool value,string message="Usability assertion failed") { if(!value) throw new Exception(message); }
    static string Area() { var path=Path.Combine(Path.GetTempPath(),"CarArchive-interactions-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path; }
    public static void Register(Action<string,Func<Task>> test)
    {
        test("Crop upload: every mode and deep-check sends only the cropped representative at a bounded image size",async ()=>
        {
            var images=new CropImages();var requests=0;
            using var http=new HttpClient(new CropHandler(async request=>
            {
                using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var uploaded=body.RootElement.GetProperty("input").EnumerateArray().Where(e=>e.GetProperty("type").GetString()=="image").ToArray();
                Check(uploaded.Length==1);requests++;
                return new(System.Net.HttpStatusCode.OK) { Content=new StringContent("{\"output_text\":\"{\\\"brand\\\":\\\"Example\\\"}\"}") };
            }));
            var backend=new GeminiService(http,images,()=>"synthetic-key",()=>{});
            var representative=new Photo { Crop=new(.2,.1,.4,.5) };var photos=new[] { representative,new Photo(),new Photo() };
            foreach(var mode in Enum.GetValues<RecognitionMode>()) foreach(var deep in new[]{false,true})
            {
                images.Calls.Clear();await backend.RecognizeAsync(photos,new() { Mode=mode },deep,default);
                Check(images.Calls.Count==(mode==RecognitionMode.Accurate?2:1) && images.Calls.All(c=>ReferenceEquals(c.Photo,representative) && c.Edge==1280) && ReferenceEquals(images.Calls[0].Photo,representative) && images.Calls[0].Edge==1280);
            }
            Check(requests==10 && photos.Length==3 && representative.Crop==new CropRect(.2,.1,.4,.5));
        });
        test("Compact search: s63coupe, m3competition and 911carreragts segment against catalog words",()=>
        {
            var index=new CategorySearchIndex([new("Mercedes-AMG","S 63 Coupe (C217)","coupe",true),new("Mercedes-AMG","S 63 Sedan (W222)","sedan",true),new("Mercedes-AMG","S 65 Coupe (C217)","65",true),new("BMW","M3 Competition (G80)","m3",true),new("Porsche","911 Carrera GTS (992)","911",true)]);
            foreach(var (query,path) in new[]{("s63","coupe"),("s63coupe","coupe"),("s63co","coupe"),("m3competition","m3"),("m3comp","m3"),("911carreragts","911"),("911carr","911")}) Check(index.Match(query,"")[0].TargetPath==path,query);
            Check(index.Match("s65coupe","")[0].TargetPath=="65");return Task.CompletedTask;
        });
        test("Brand catalog includes existing empty directories and excludes remembered-only brands",()=>
        {
            var root=Area();Directory.CreateDirectory(Path.Combine(root,"BMW"));Directory.CreateDirectory(Path.Combine(root,"Audi","A4"));
            var catalog=CategoryCatalogService.Scan(root,[new("x","Absent","Model")],new Dictionary<string,string>());
            Check(catalog.BrandDirectories!.SequenceEqual(new[]{"Audi","BMW"}) && catalog.Items.Count==1);
            var search=new CategorySearchIndex(catalog.BrandDirectories.Select(b=>new CategorySuggestion(b,b,Path.Combine(root,b),true)));
            Check(search.Match("bwm","")[0].BrandDirectory=="BMW");return Task.CompletedTask;
        });
        test("Latin spelling handles accents, ligatures and letters without decompositions",()=>
        {
            Check(LatinText.English("Coupé Škoda Citroën Æther Œuvre Straße Łódź Øresund Þór Đorđe") =="Coupe Skoda Citroen AEther OEuvre Strasse Lodz Oresund Thor Dorde");
            var identity=new CarIdentity { Brand="Example",Variant="Mégane Coupé Édition",Generation="3rd" };var raw=identity.Variant;
            Check(NamingService.DefaultName(identity)=="Megane Coupe Edition (3rd)" && identity.Variant==raw);return Task.CompletedTask;
        });
        test("Selected-photo copy shares originals while clearing target, evidence and crops",()=>
        {
            var asset=new ImageAsset { OriginalPath="original.jpg" };var a=new Photo { Path="original.jpg",Crop=new(0,0,.3,.3) };a.Attach(asset);
            var b=a.CopyReference();var source=new PhotoGroup { Photos=[a,b],FolderName="Existing",RepresentativeId=b.Id,Confirmed=new() { Variant="Old" },Suggestion=new() };
            var copy=GroupPhotoEditor.Copy(source,[b]);
            Check(source.Photos.Count==2 && copy.Photos.Count==1 && copy.Photos[0].Id!=b.Id && copy.Photos[0].AssetId==b.AssetId && copy.Photos[0].Crop is null);
            Check(copy.FolderName=="" && copy.Confirmed.Variant=="" && copy.Suggestion is null);return Task.CompletedTask;
        });
        test("Photo-edit validation rejects stale references and groups with active tasks",()=>
        {
            var p=new Photo();var group=new PhotoGroup { Photos=[p] };
            try { GroupPhotoEditor.Copy(group,[new() { Id=p.Id }]);throw new Exception("Forged reference accepted"); }catch(InvalidOperationException) { }
            group.TaskState=GroupTaskState.Running;
            try { GroupPhotoEditor.Copy(group,[p]);throw new Exception("Active group accepted"); }catch(InvalidOperationException) { }
            return Task.CompletedTask;
        });
        test("Crash journal discards normal sessions",()=>
        {
            var root=Area();using var journal=new CrashJournal(root);journal.Record("Action.Test");journal.Flush();Check(File.Exists(Path.Combine(root,"session-pending.json")));journal.Complete();
            Check(!Directory.EnumerateFiles(root).Any());return Task.CompletedTask;
        });
        test("Crash journal captures bounded recent actions and redacts credential-like text",()=>
        {
            var root=Area();using var journal=new CrashJournal(root,8);for(var i=0;i<20;i++) journal.Record("Action."+i);
            journal.Capture("Test",new Exception("api_key=secret-value"));using var report=JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(root,"crash-*.json").Single()));
            var rows=report.RootElement.GetProperty("Session").GetProperty("RecentActions");Check(rows.GetArrayLength()==8 && rows[6].GetProperty("Action").GetString()=="Action.19");
            Check(!report.RootElement.GetRawText().Contains("secret-value"));journal.Flush();Check(!File.Exists(Path.Combine(root,"session-pending.json")));return Task.CompletedTask;
        });
        test("Crash journal recovers an interrupted session on the following startup",()=>
        {
            var root=Area();using(var first=new CrashJournal(root)) { first.Record("Before.NativeFailure");first.Flush(); }
            using var second=new CrashJournal(root);var report=File.ReadAllText(Directory.GetFiles(root,"crash-*.json").Single());
            Check(report.Contains("UnexpectedExit") && report.Contains("Before.NativeFailure"));second.Complete();Check(!File.Exists(Path.Combine(root,"session-pending.json")));return Task.CompletedTask;
        });
    }
    private sealed class CropImages:IPhotoService
    {
        public List<(Photo Photo,int Edge)> Calls { get; }=[];
        public Task<Photo> ImportAsync(string path,CancellationToken ct)=>Task.FromResult(new Photo { Path=path });
        public Task<byte[]> AnalysisJpegAsync(Photo photo,int longestEdge,CancellationToken ct) { Calls.Add((photo,longestEdge));return Task.FromResult(new byte[]{1,2,3}); }
    }
    private sealed class CropHandler(Func<HttpRequestMessage,Task<HttpResponseMessage>> response):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>response(request); }
}
