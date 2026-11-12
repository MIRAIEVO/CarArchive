#if DEBUG
using System.Text.Json;
using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CarArchive;
public sealed partial class MainPage
{
    private async Task RunV13VerificationAsync(string directory)
    {
        directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);var checks=new List<string>();
        void Check(bool value,string label) { if(!value) throw new Exception(label);checks.Add(label); }
        try
        {
            Check(ViewModels.MainViewModel.DataDirectory.StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) && VM.Groups.Count==0,"Isolated native workspace");
            var path=await FixtureImageAsync(Path.Combine(directory,"portrait.jpg"),300,600);
            await VM.ImportAsync([path],true,default);await VM.BackgroundImport;var group=VM.Selected!;
            var backend=new V13Recognition();VM.Recognition=backend;
            await VM.RecognizeAsync(group,false,default,new() { Mode=RecognitionMode.Fast });
            var original=group.Suggestion;var before=group.Confirmed.NamingKey;var oldName=group.FolderName;
            await VM.RunGroupAsync(group,"深入查证",false,ct=>VM.RecognizeAsync(group,true,ct,new() { Mode=RecognitionMode.Fast }));
            Check(backend.IdentifyCalls==1 && backend.VerifyCalls==1,"Deep verification uses explicit service without another identification");
            Check(ReferenceEquals(group.Suggestion,original) && group.Confirmed.NamingKey==before && group.FolderName==oldName,"Deep report never automatically overwrites classification or folder");
            RenderSelection();Check(ApiResultExpander.IsExpanded && VerificationBanner.Severity==InfoBarSeverity.Error && VerificationDetails.Text.Contains("照片对照"),"Native API results display verdict, feature comparison and auto-expand");
            Check(VerificationDetails.Text.Contains("未知"),"Missing provider usage is shown as unknown");
            await CaptureVisualAsync(VerificationBanner,Path.Combine(directory,"v13-verdict-failed.png"));
            await CaptureVisualAsync(ApiResultExpander,Path.Combine(directory,"v13-api-failed.png"));
            group.Verification!.Status=VerificationStatus.Passed;RenderVerification();
            await CaptureVisualAsync(VerificationBanner,Path.Combine(directory,"v13-verdict-passed.png"));
            await CaptureVisualAsync(ApiResultExpander,Path.Combine(directory,"v13-api-passed.png"));
            group.Verification.Status=VerificationStatus.Failed;RenderVerification();
            VM.ApplyVerification(group);Check(group.Confirmed.Variant=="Model Sport" && group.FolderName.StartsWith("Model Sport"),"Explicit adoption updates identity and locally generates folder");
            group.ManualName=true;group.FolderName="My reviewed folder";await VM.RecognizeAsync(group,true,default);VM.ApplyVerification(group);
            Check(group.FolderName=="My reviewed folder" && group.VerificationHistory.Count==1,"Adoption preserves manual name and prior verification history");
            var other=VM.Duplicate(group);VM.Selected=other;RenderSelection();backend.Failure=true;
            await VM.RunGroupAsync(group,"深入查证",false,ct=>VM.RecognizeAsync(group,true,ct));
            Check(group.TaskState==GroupTaskState.Failed && group.Verification?.Status==VerificationStatus.Incomplete && ReferenceEquals(group.Suggestion,original),"Incomplete verification retains prior identification and marks task failed");
            Check(VM.Selected==other && other.Verification is null,"Background completion never changes another group's selection or report");
            VM.Selected=group;RenderSelection();Check(!AdoptVerificationButton.IsEnabled,"Incomplete report cannot be adopted");backend.Failure=false;
            Check(VisualCommands().SequenceEqual(new[]{"设置","命名学习","操作记录与恢复","撤销最近归档"}),"Overflow exposes exactly four commands");
            Check(!Descendants(PreviewPane).OfType<Button>().Any(b=>b.Content is string content && content is "上一组" or "下一组"),"Bottom navigation buttons removed");
            ResizeThumbnails(800);Check(ThumbnailRow.Height.Value==147,"Thumbnail row uses 120px preview plus caption");ResizeThumbnails(550);Check(ThumbnailRow.Height.Value==123,"Short window uses 96px thumbnail preview");
            ResizeThumbnails(800);VM.Selected=group;RenderSelection();
            foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})
            { RequestedTheme=theme;if(narrowLayout) PaneBox.SelectedIndex=2;await Task.Delay(150);await CaptureVisualAsync(this,Path.Combine(directory,"v13-review-"+theme+".png")); }
            Settings_Click(this,new RoutedEventArgs());await Task.Delay(200);
            var settings=VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).SelectMany(p=>p.Child is null?[]:Descendants(p.Child)).OfType<ContentDialog>().FirstOrDefault();
            Check(settings is not null,"Native settings dialog opens");
            if(settings is not null)
            {
                var headers=Descendants(settings).OfType<Expander>().Select(e=>e.Header?.ToString()).ToList();
                Check(headers.Contains("识别与费用") && headers.Contains("命名学习") && headers.Contains("数据与维护"),"Settings use three focused sections");
                await CaptureVisualAsync(settings,Path.Combine(directory,"v13-settings.png"));settings.Hide();await Task.Delay(100);
            }
            Rules_Click(this,new RoutedEventArgs());await Task.Delay(200);
            var learning=VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).SelectMany(p=>p.Child is null?[]:Descendants(p.Child)).OfType<ContentDialog>().FirstOrDefault();
            Check(learning is not null,"Native local learning manager opens");
            if(learning is not null) { await CaptureVisualAsync(learning,Path.Combine(directory,"v13-learning.png"));learning.Hide();await Task.Delay(100); }
            App.MainWindow!.AppWindow.Resize(new(850,900));if(narrowLayout) PaneBox.SelectedIndex=1;await Task.Delay(200);
            Check(ArchiveFooter.ActualHeight>0 && ThumbnailRow.Height.Value>=123,"Narrow workspace retains footer and enlarged previews");
            await CaptureVisualAsync(this,Path.Combine(directory,"v13-narrow.png"));
            PaneBox.SelectedIndex=2;await Task.Delay(100);Check(ArchiveFooter.ActualHeight>0,"Narrow review keeps archive actions fixed");
            Check(EditorScroll.ViewportHeight>=80,"Short high-DPI review preserves an editable field viewport");
            await CaptureVisualAsync(this,Path.Combine(directory,"v13-narrow-review.png"));
            var archivedOriginal=group.Photos[0].OriginalPath;File.Delete(archivedOriginal);
            var destination=Path.Combine(directory,"archived-copy.jpg");await FixtureImageAsync(destination,300,600);group.Photos[0].Path=destination;other.Photos[0].Path=destination;
            VM.Selected=other;RenderSelection();
            var retainedThumbnail=group.Photos[0].ThumbnailPath;await ClearRebuildableCacheAsync();
            Check(File.Exists(retainedThumbnail) && File.Exists(destination),"Cache cleanup preserves archived thumbnails when original location is gone");
            await VM.FlushWritesAsync();File.WriteAllText(Path.Combine(directory,"v13-verification.json"),JsonSerializer.Serialize(new { Status="Passed",Checks=checks,RealApiCalls=0 },new JsonSerializerOptions(StateStore.Json) { WriteIndented=true }));
            App.MainWindow.Title="CarArchive · 新版窗口验证";
        }
        catch(Exception error) { File.WriteAllText(Path.Combine(directory,"v13-verification.json"),JsonSerializer.Serialize(new { Status="Failed",Error=error.ToString(),Checks=checks },StateStore.Json));VM.Status=error.Message; }
        IEnumerable<string> VisualCommands()=>Descendants(this).OfType<CommandBar>().First().SecondaryCommands.OfType<AppBarButton>().Select(b=>b.Label);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i))) yield return child; }
    private sealed class V13Recognition : IRecognitionService
    {
        public int IdentifyCalls,VerifyCalls;public bool Failure;
        public Task<RecognitionResult> RecognizeAsync(IReadOnlyList<Photo> photos,AppSettings settings,bool again,CancellationToken ct)
        { IdentifyCalls++;return Task.FromResult(new RecognitionResult { Identity=new() { Brand="Example",Series="Model",Variant="Model",Generation="2nd" },Observations="合成测试照片" }); }
        public Task<VerificationReport> VerifyAsync(IReadOnlyList<Photo> photos,AppSettings settings,CarIdentity candidate,string questions,CancellationToken ct)
        {
            VerifyCalls++;var correction=candidate.Clone();correction.Variant="Model Sport";
            return Task.FromResult(new VerificationReport { Candidate=candidate.Clone(),SuggestedIdentity=correction,Status=Failure?VerificationStatus.Incomplete:VerificationStatus.Failed,Error=Failure?"Synthetic disconnected stage":"",Summary="合成特征比对结果",Features=[new() { Id="series",Field="series",Expected="测试灯组",ReferenceUrl="https://example.com/reference",Critical=true }],Checks=[new() { FeatureId="series",Match="contradiction",Observation="照片中灯组不同" }],Stages=[new() { Name="检索特征",SearchEnabled=true,Result=new RecognitionResult { Sources=[new("Fixture source","https://example.com/reference","测试灯组")],Queries=["fixture"] } },new() { Name="照片比对",SearchEnabled=false }] });
        }
    }
}
#endif
