#if DEBUG
using System.Text.Json;
using CarArchive.Core;
using Microsoft.UI.Xaml;

namespace CarArchive;

public sealed partial class MainPage
{
    private async Task RunNamingVerificationAsync(string directory)
    {
        directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);var report=new Dictionary<string,object>();var checks=new List<string>();
        void Check(bool value,string message) { if(!value) throw new Exception(message);checks.Add(message); }
        try
        {
            if(!Path.GetFullPath(ViewModels.MainViewModel.DataDirectory).StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || VM.Groups.Count>0)
                throw new InvalidOperationException("Naming verification requires a new isolated data directory.");
            var root=Path.Combine(directory,"library");
            Directory.CreateDirectory(Path.Combine(root,"BMW","6 Series Coupe Coupe (F13 Facelift)"));
            Directory.CreateDirectory(Path.Combine(root,"Rolls-Royce","Phantom (VII)"));
            Directory.CreateDirectory(Path.Combine(root,"Rolls-Royce","Silver Spirit (1st)"));
            Directory.CreateDirectory(Path.Combine(root,"Mercedes-AMG","A 35 Hatchback (W177)"));
            var source=Path.Combine(directory,"synthetic.jpg");await File.WriteAllBytesAsync(source,await Services.PhotoService.ConnectivityImageAsync());var hash=await ArchiveService.HashAsync(source);
            await VM.ImportAsync([source],true,default);await VM.BackgroundImport;var group=VM.Groups[0];var other=VM.Duplicate(group);other.FolderName="Other group unchanged";
            VM.State.Settings.ArchiveRoot=root;
            group.Confirmed=new() { Brand="BMW",Series="6 Series",Variant="6 Series Coupe",Body="Coupe",Generation="3rd Generation",Code="F13",Phase="Facelift" };
            VM.State.Names.Add(new(group.Confirmed.NamingKey,"BMW","6 Series Coupe Coupe (F13 Facelift)"));
            VM.Selected=group;RenderSelection();await VM.EnsureNamingGuideAsync(true);Generate_Click(this,new());
            Check(group.FolderName=="6 Series Coupe (F13 LCI)","Generate button uses local guide, complete model, and BMW LCI without duplicate Body.");
            var key=group.Confirmed.NamingKey;BodyEdit.Text="Sedan";Generate_Click(this,new());
            Check(group.FolderName=="6 Series Coupe (F13 LCI)" && group.Confirmed.NamingKey==key,"Changing the informational Body field does not change naming or its key.");
            group.Confirmed=new() { Brand="Rolls-Royce",Series="Silver Spirit",Variant="Silver Spirit",Generation="1st",ProductionYears="1980-1989" };RenderSelection();Generate_Click(this,new());
            Check(group.FolderName=="Silver Spirit (1980-1989)" && YearsEdit.Text=="1980-1989","Production-years editor participates in classic naming and survives rendering.");
            group.Confirmed=new() { Brand="Rolls-Royce",Series="Phantom",Variant="Phantom EWB",Generation="8th",Phase="Facelift" };RenderSelection();Generate_Click(this,new());
            Check(group.FolderName=="Phantom EWB (VIII Facelift)","Phantom uses roman generation VIII.");
            group.Confirmed=new() { Brand="BMW",Series="3 Series",Variant="BMW M3 Competition",OfficialName="BMW M3 Competition Sedan 50 Jahre Edition",Package="Competition",Generation="6th",Code="G80",Phase="Facelift" };RenderSelection();Generate_Click(this,new());
            Check(group.FolderName=="M3 Competition Sedan 50 Jahre Edition (6th LCI)","Complete official marketing name retains body, package and special edition without historic abbreviation.");
            group.Confirmed=new() { Brand="Porsche",Series="911",Variant="Porsche 911 Carrera GTS Cabriolet",Generation="8th",Code="992.1",Phase="Facelift" };RenderSelection();Generate_Click(this,new());
            Check(group.FolderName=="911 Carrera GTS Cabriolet (992.2)","911 facelift uses .2, omits .1 and Facelift, and preserves full model.");
            VM.State.BrandFolders["Mercedes-AMG"]="Mercedes";
            group.Confirmed=new() { Brand="Mercedes-AMG",Series="A-Class",Variant="Mercedes-AMG A 45 S Hatchback 4MATIC+ Edition 1",Generation="4th",Code="W177" };RenderSelection();Generate_Click(this,new());
            Check(group.FolderName=="A 45 S Hatchback Edition 1 (W177)" && group.TargetBrand=="Mercedes-AMG","Actual AMG directory overrides a missing legacy mapping and omits the drivetrain marker.");
            group.ManualName=true;group.FolderName="Manually preserved";RenderSelection();var actual=VM.Recognition;VM.Recognition=new NamingVerificationRecognizer();
            await VM.RunGroupAsync(group,"本地模拟识别",false,ct=>VM.RecognizeAsync(group,false,ct));VM.Recognition=actual;
            Check(group.FolderName=="Manually preserved" && other.FolderName=="Other group unchanged","Recognition preserves a manual name and another group's independent state.");
            group.ManualName=false;group.Confirmed=new() { Brand="BMW",Series="6 Series",Variant="6 Series Coupe",Body="Coupe",Generation="3rd",Code="F13",Phase="Facelift" };RenderSelection();Generate_Click(this,new());
            foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
            {
                RequestedTheme=theme;await Task.Delay(150);
                var footer=ArchiveFooter.TransformToVisual(BodyGrid).TransformPoint(new(0,ArchiveFooter.ActualHeight)).Y;
                Check(Math.Abs(footer-BodyGrid.ActualHeight)<2,"Pinned archive footer is accessible in "+theme+" theme.");
            }
            App.MainWindow!.AppWindow.Resize(new(850,900));PaneBox.SelectedIndex=2;await Task.Delay(250);
            Check(narrowLayout && EditorPane.Visibility==Visibility.Visible && ArchiveFooter.ActualHeight>0,"Narrow review workspace retains the fixed archive actions.");
            App.MainWindow.AppWindow.Resize(new(1400,950));RequestedTheme=ElementTheme.Light;await Task.Delay(250);
            Check(File.Exists(source) && await ArchiveService.HashAsync(source)==hash && VM.Store.Jobs().Count==0,"Verification creates no archive jobs and preserves synthetic original bytes.");
            VM.Save();await VM.FlushWritesAsync();var reloaded=VM.Store.Load();
            Check(reloaded.Groups.Count==2 && reloaded.Groups.Any(g=>g.FolderName=="6 Series Coupe (F13 LCI)"),"Final names and independent groups persist to SQLite.");
            report["Status"]="Passed";report["Checks"]=checks;report["GuideDirectory"]=VM.NamingGuideDirectory;report["FooterBottom"]=ArchiveFooter.TransformToVisual(BodyGrid).TransformPoint(new(0,ArchiveFooter.ActualHeight)).Y;
            report["BodyHeight"]=BodyGrid.ActualHeight;report["RasterizationScale"]=XamlRoot.RasterizationScale;report["ApiCalls"]=VM.State.BatchCalls;
            VM.Status="本地命名验证通过 · 合成图片 · 未调用 Gemini";App.MainWindow.Title="CarArchive · 本地命名验证";
        }
        catch(Exception error) { report["Status"]="Failed";report["Error"]=error.ToString();report["PassedChecks"]=checks;VM.Status="本地命名验证失败："+error.Message; }
        File.WriteAllText(Path.Combine(directory,"native-naming-verification.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions(StateStore.Json) { WriteIndented=true }));
    }
    private sealed class NamingVerificationRecognizer : IRecognitionService
    {
        public Task<RecognitionResult> RecognizeAsync(IReadOnlyList<Photo> photos,AppSettings settings,bool verifyAgain,CancellationToken ct) => Task.FromResult(new RecognitionResult { Identity=new() { Brand="Example",Series="Changed model",Generation="2nd" } });
        public Task<List<List<string>>> GroupAsync(IReadOnlyList<Photo> photos,AppSettings settings,CancellationToken ct) => throw new NotSupportedException();
    }
}
#endif
