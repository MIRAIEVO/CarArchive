#if DEBUG
using System.Diagnostics;
using System.Text.Json;
using CarArchive.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace CarArchive;

public sealed partial class MainPage
{
    private async Task RunVerificationAsync(string directory)
    {
        directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);
        var report=new Dictionary<string,object>();var checks=new List<string>();
        void Stage(string name) { report["Phase"]=name;File.WriteAllText(Path.Combine(directory,"verification-progress.json"),JsonSerializer.Serialize(report)); }
        var heartbeat=DispatcherQueue.CreateTimer();heartbeat.Interval=TimeSpan.FromMilliseconds(50);
        var last=Stopwatch.GetTimestamp();double maxDelay=0;
        heartbeat.Tick+=(_,_)=> { var now=Stopwatch.GetTimestamp();maxDelay=Math.Max(maxDelay,Stopwatch.GetElapsedTime(last,now).TotalMilliseconds);last=now; };heartbeat.Start();
        var actualRecognizer=VM.Recognition;
        void Check(bool value,string message) { if(!value) throw new Exception(message); }
        try
        {
            Check(Path.GetFullPath(ViewModels.MainViewModel.DataDirectory).StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Verification requires an isolated data directory.");
            Check(VM.Groups.Count==0,"Verification data directory must be empty.");
            VM.Archive.DiagnosticTrace=message=>File.AppendAllText(Path.Combine(directory,"file-operations.log"),DateTimeOffset.Now.ToString("O")+" "+message+"\n");
            Stage("Creating fixtures");
            var fixture=Path.Combine(directory,"synthetic-12mp");Directory.CreateDirectory(fixture);
            VM.Status="开发验证：正在创建 500 张 4000×3000 合成 JPEG…";
            var bytes=await Task.Run(async ()=> {
                const int w=4000,h=3000;var pixels=new byte[w*h*4];
                for(var y=0;y<h;y++) for(var x=0;x<w;x++) {
                    var car=x>550 && x<3450 && y>1300 && y<2200;var roof=x>1250 && x<2750 && y>850 && y<=1300;
                    var wheel=(long)(x-1200)*(x-1200)+(long)(y-2200)*(y-2200)<260L*260 || (long)(x-2800)*(x-2800)+(long)(y-2200)*(y-2200)<260L*260;
                    var i=(y*w+x)*4;pixels[i]=(byte)(wheel?30:car||roof?185:190+x%40);pixels[i+1]=(byte)(wheel?30:car||roof?105:195+y%40);pixels[i+2]=(byte)(wheel?30:car||roof?45:200+(x+y)%40);pixels[i+3]=255;
                }
                using var memory=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId,memory);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,w,h,96,96,pixels);await encoder.FlushAsync();memory.Seek(0);
                using var reader=new DataReader(memory.GetInputStreamAt(0));await reader.LoadAsync((uint)memory.Size);var data=new byte[(int)memory.Size];reader.ReadBytes(data);return data;
            });
            await Task.Run(async ()=> { for(var i=0;i<500;i++) await File.WriteAllBytesAsync(Path.Combine(fixture,$"IMG_{i+1:D4}.jpg"),bytes); });
            var files=Directory.EnumerateFiles(fixture).ToList();var watch=Stopwatch.StartNew();
            await VM.ImportAsync(files,false,default);RenderSelection();
            report["Register500Seconds"]=watch.Elapsed.TotalSeconds;
            Stage("Registered; preparing thumbnails");
            Check(VM.Groups.Sum(g=>g.Photos.Count)==500,"Fast registration lost files.");
            foreach(var group in VM.Groups.Take(6)) VM.MarkTouched(group);
            await Task.Delay(250);
            App.MainWindow!.AppWindow.Hide();
            report["EarlyHdPreviewCount"]=Directory.GetFiles(Path.Combine(ViewModels.MainViewModel.DataDirectory,"cache"),"*.jpg").Count(p=>!p.EndsWith("-thumb.jpg"));
            Check((int)report["EarlyHdPreviewCount"]<10,"Import eagerly built too many high-resolution previews.");
            await VM.BackgroundImport;report["BackgroundThumbnails500Seconds"]=watch.Elapsed.TotalSeconds;
            Stage("Measuring eager comparison");
            Check(VM.Groups.SelectMany(g=>g.Photos).All(p=>p.Error.Length==0 && File.Exists(p.ThumbnailPath)),"Background thumbnails failed.");
            checks.Add("500 12MP files registered without waiting for thumbnails; HD previews are on demand.");
            VM.Status="开发验证：对同一批 500 张图片测量旧式全量预览流程…";
            var eager=new Services.PhotoService(Path.Combine(directory,"eager-comparison-cache"));var eagerWatch=Stopwatch.StartNew();
            if(Environment.GetEnvironmentVariable("CARARCHIVE_VERIFY_SKIP_EAGER")!="1") foreach(var chunk in files.Chunk(4))
                await Task.WhenAll(chunk.Select(async path=> { var p=await eager.ImportAsync(path,default);await eager.EnsurePreviewAsync(p,default);await eager.EnsureThumbnailAsync(p,default); }));
            report["EagerPipeline500Seconds"]=eagerWatch.Elapsed.TotalSeconds;
            Stage("Testing concurrency");
            report["ComparisonNote"]="Same 500 synthetic 12MP files; eager comparison reproduces metadata + all HD previews + thumbnails with the new decoder, not the old executable.";
            var first=VM.Groups.Take(4).ToList();VM.Merge(first);var merged=VM.Selected!;VM.Split(merged,merged.Photos.Take(2).ToList());var a=VM.Selected!;
            a.Photos[0].Crop=new(.25,.25,.5,.5);var b=VM.Duplicate(a);
            Check(b.Photos.All(p=>p.Crop is null) && b.FolderName=="" && b.Suggestion is null && a.Photos[0].Asset==b.Photos[0].Asset,"Duplicate group was not independent.");
            VM.SelectPhoto(a,a.Photos[1]);Check(a.RepresentativeId==a.Photos[1].Id && a.LastViewedId==a.Photos[1].Id,"Click-to-representative failed.");
            var cropBytes=await VM.Images.AnalysisJpegAsync(a.Photos[0],2048,default);
            using(var stream=new InMemoryRandomAccessStream()) {
                using(var writer=new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(cropBytes);await writer.StoreAsync(); }
                stream.Seek(0);var decoder=await BitmapDecoder.CreateAsync(stream);Check(decoder.PixelWidth==1024 && decoder.PixelHeight==768,"12MP normalized crop dimensions failed.");
            }
            checks.Add("Split/merge preserve references; duplicate has independent crop, identity and naming; click sets representative.");
            var fake=new VerificationRecognizer();VM.Recognition=fake;VM.State.Settings.Mode=RecognitionMode.FieldsOnly;
            var c=VM.Groups.First(g=>g!=a && g!=b);var d=VM.Groups.First(g=>g!=a && g!=b && g!=c);
            var e=VM.Groups.First(g=>g!=a && g!=b && g!=c && g!=d);
            foreach(var group in new[]{a,b,c,d,e}) VM.MarkTouched(group);
            VM.State.Settings.ArchiveRoot=Path.Combine(directory,"archive");
            var taskA=VM.RunGroupAsync(a,"验证查询 A",false,ct=>VM.RecognizeAsync(a,false,ct));
            var taskB=VM.RunGroupAsync(b,"验证查询 B",false,ct=>VM.RecognizeAsync(b,false,ct));
            Check(a.IsWorking && b.IsWorking && fake.Calls==2,"Two recognition requests did not run concurrently.");
            VM.Selected=c;RenderSelection();BrandEdit.Text="Manual Brand";SeriesEdit.Text="Manual C";FolderEdit.Text="Manual C Folder";TargetBrandEdit.Text="Manual Brand";FlushEditor();
            fake.Complete(b.Photos[0].Id,new() { Brand="Fixture Brand",Series="Car Beta",Code="TEST" });await taskB;
            Check(VM.Selected==c && BrandEdit.Text=="Manual Brand" && c.FolderName=="Manual C Folder" && b.FolderName=="Car Beta (TEST)","Out-of-order completion overwrote the selected editor or failed automatic naming.");
            var taskD=VM.RunGroupAsync(d,"验证查询 D",false,ct=>VM.RecognizeAsync(d,false,ct));
            var releaseFile=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var taskC=VM.RunGroupAsync(c,"验证归档 C",true,async ct=> { await releaseFile.Task.WaitAsync(ct);await VM.ArchiveAsync(c,ct); });
            Check(c.IsWorking && a.IsWorking && d.IsWorking,"File work did not overlap recognition.");
            VM.Selected=e;RenderSelection();BrandEdit.Text="Editable E";FolderEdit.Text="E stays independent";FlushEditor();VM.SaveGroup(e);
            releaseFile.SetResult();await taskC;
            Check(VM.Selected==e && BrandEdit.Text=="Editable E" && e.FolderName=="E stays independent" && c.Archived,"Background archive changed the current editor or selection.");
            fake.Complete(d.Photos[0].Id,new() { Brand="Fixture Brand",Series="Car Delta" });await taskD;
            fake.Complete(a.Photos.First(p=>p.Id==a.RepresentativeId).Id,new() { Brand="Fixture Brand",Series="Car Alpha",Code="TEST" });await taskA;
            Check(a.TaskState==GroupTaskState.Completed && b.TaskState==GroupTaskState.Completed && c.TaskState==GroupTaskState.Completed && fake.Calls==3,"Task status or request count failed.");
            checks.Add("A/B out-of-order recognition, C archive and D recognition overlap while E remains editable; no editor/selection crossover; auto naming succeeds.");
            Stage("Concurrency passed; testing shared archives");
            var originalPaths=a.Photos.Select(p=>p.OriginalPath).ToList();var hashes=await Task.WhenAll(originalPaths.Select(p=>ArchiveService.HashAsync(p)));
            await VM.RunGroupAsync(a,"归档 A",true,ct=>VM.ArchiveAsync(a,ct));Check(originalPaths.All(File.Exists),"First shared archive deleted originals.");
            await VM.RunGroupAsync(b,"归档 B",true,ct=>VM.ArchiveAsync(b,ct));Check(originalPaths.All(p=>!File.Exists(p)),"Last shared archive did not release originals.");
            var bPaths=b.Photos.Select(p=>p.Path).ToList();
            Stage("Both shared archives passed; undoing A");
            var jobA=VM.Store.Jobs().First(j=>j.GroupId==a.Id && j.Direction=="Archive");await VM.RunGroupAsync(a,"撤销 A",true,ct=>VM.ResumeOrUndoAsync(jobA,true,ct));
            Check(originalPaths.All(File.Exists) && bPaths.All(File.Exists),"Undo A affected B's copies.");
            var restored=await Task.WhenAll(originalPaths.Select(p=>ArchiveService.HashAsync(p)));Check(hashes.SequenceEqual(restored),"Shared archive/undo modified originals.");
            Stage("Shared archive and undo passed; checking layout");
            checks.Add("One photo can be filed into two directories; first retains source, last removes source, undo A preserves B and original SHA-256.");
            VM.Recognition=actualRecognizer;VM.Selected=e;RenderSelection();VM.Save();await VM.FlushWritesAsync();
            var reloaded=VM.Store.Load();Check(reloaded.Groups.Sum(g=>g.Photos.Count)==502 && reloaded.Assets.Count==500,"Incremental persistence lost shared references/assets.");
            checks.Add("SQLite reload preserves 502 references to 500 original assets.");
            App.MainWindow!.AppWindow.Show();await Task.Delay(250);
            var footerBefore=ArchiveFooter.TransformToVisual(BodyGrid).TransformPoint(new Windows.Foundation.Point()).Y;
            EditorScroll.ChangeView(null,10000,null);await Task.Delay(100);
            var footerAfter=ArchiveFooter.TransformToVisual(BodyGrid).TransformPoint(new Windows.Foundation.Point()).Y;
            Check(Math.Abs(footerBefore-footerAfter)<1 && footerAfter+ArchiveFooter.ActualHeight<=BodyGrid.ActualHeight+1,"Archive footer moved with the scroll region.");
            report["PreviewHeightLogical"]=PreviewHost.ActualHeight;report["FooterBottomLogical"]=footerAfter+ArchiveFooter.ActualHeight;report["BodyHeightLogical"]=BodyGrid.ActualHeight;
            checks.Add("Archive controls stay fixed at the bottom while recognition details scroll.");
            report["Status"]="Passed";VM.Status="第二版开发验证通过：500 张 12MP 合成图、分组并行、独立编辑、共享归档及固定操作区。";
        }
        catch(Exception error) { report["Status"]="Failed";report["Error"]=error.ToString();VM.Status="开发验证失败："+error.Message; }
        finally {
            VM.Recognition=actualRecognizer;VM.Archive.DiagnosticTrace=null;VM.Tasks.CancelAll();App.MainWindow!.AppWindow.Show();heartbeat.Stop();report["MaxUiHeartbeatGapMs"]=maxDelay;report["Checks"]=checks;
            report["RealGeminiTokenComparison"]="Not performed: no user API key used; transport tests validate one image, nine fields, one request and the configured budget.";
            report["RealVehicleAccuracy"]="Not tested; requires real labeled car photos and an API key.";
            await File.WriteAllTextAsync(Path.Combine(directory,"desktop-verification.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented=true }));
        }
    }
    private sealed class VerificationRecognizer:IRecognitionService
    {
        private readonly Dictionary<string,TaskCompletionSource<RecognitionResult>> pending=[];
        public int Calls { get;private set; }
        public Task<RecognitionResult> RecognizeAsync(IReadOnlyList<Photo> photos,AppSettings settings,bool verifyAgain,CancellationToken ct) {
            Calls++;var id=photos[0].Id;var gate=new TaskCompletionSource<RecognitionResult>(TaskCreationOptions.RunContinuationsAsynchronously);pending[id]=gate;return gate.Task.WaitAsync(ct);
        }
        public void Complete(string photoId,CarIdentity identity)=>pending[photoId].SetResult(new() { Identity=identity,Model="verification-no-network" });
        public Task<List<List<string>>> GroupAsync(IReadOnlyList<Photo> photos,AppSettings settings,CancellationToken ct)=>Task.FromResult(photos.Select(p=>new List<string>{p.Id}).ToList());
    }
}
#endif
