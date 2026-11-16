#if DEBUG
using System.Text.Json;
using CarArchive.Core;
namespace CarArchive;
public sealed partial class MainPage
{
 private async Task RunV12VerificationAsync(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<string>();
  void Check(bool value,string text) {if(!value)throw new Exception(text);checks.Add(text);}
  try {
   Check(VM.Groups.Count==0,"Isolated empty workspace");
   var files=new List<string>();for(var i=0;i<3;i++)files.Add(await FixtureImageAsync(Path.Combine(directory,$"photo{i}.jpg"),i==0?200u:400u,i==0?400u:200u));
   await VM.ImportAsync(files,true,default);await VM.BackgroundImport;var group=VM.Selected!;RenderSelection();
   Check(group.Mode==RecognitionMode.FieldsOnly && !group.UploadMultiple,"Per-group default mode");
   Check(group.Photos.Select(p=>p.Path).SequenceEqual(files),"Import indexes originals without copying");
   group.LastViewedId=group.Photos[1].Id;group.UploadMultiple=true;Check(UploadSelection.Select(group).Single().Id==group.Photos[1].Id,"Multi fallback to current");
   group.Photos[0].Crop=new(0,0,.5,.5);group.Photos[2].Crop=new(.1,.1,.6,.6);Check(UploadSelection.Select(group).Count==2,"Only explicit crops uploaded");
   var before=VM.CaptureEdits();VM.Split(group,[group.Photos[2]]);VM.RecordEdits(before);var split=VM.Selected!;
   Check(VM.Groups.Count==2 && group.Photos.Count==2,"Split edit");await VM.RestoreEditAsync(group.Id,false);Check(VM.Groups.Count==1 && VM.Groups[0].Photos.Count==3,"Independent split undo");
   await VM.RestoreEditAsync(group.Id,true);Check(VM.Groups.Count==2,"Split redo");group=VM.Groups.First(g=>g.Id==group.Id);
   before=VM.CaptureEdits();var copy=await VM.DuplicateAsync(group);VM.RecordEdits(before,new(){copy.Id});var copyPath=copy.Photos[0].Path;Check(File.Exists(copyPath),"Independent copy exists");
   await VM.RestoreEditAsync(copy.Id,false);Check(!File.Exists(copyPath)&&VM.Groups.All(g=>g.Id!=copy.Id),"Copy undo moves only independent files into backup");
   await VM.RestoreEditAsync(copy.Id,true);Check(File.Exists(copyPath)&&VM.Groups.Any(g=>g.Id==copy.Id),"Copy redo restores files and references");
   copy=VM.Groups.Single(g=>g.Id==copy.Id);var copyHash=await ArchiveService.HashAsync(copyPath);
   await VM.RecyclePhotosAsync(copy,[copy.Photos[0]]);Check(!File.Exists(copyPath),"Synthetic independent source recycled");
   await VM.RestoreEditAsync(copy.Id,false);copy=VM.Groups.Single(g=>g.Id==copy.Id);Check(copy.Editable && File.Exists(copyPath) && await ArchiveService.HashAsync(copyPath)==copyHash,"Recycle undo restores byte-identical editable reference");
   await VM.RestoreEditAsync(copy.Id,true);Check(!File.Exists(copyPath),"Recycle redo uses recycle-only operation");
   group=VM.Groups.First(g=>g.Id==group.Id);group.UploadMultiple=false;group.LastViewedId=group.Photos[0].Id;
   var delayed=new V12DelayedRecognition();VM.Recognition=delayed;
   var querying=VM.RunGroupAsync(group,"联网识别",false,ct=>VM.RecognizeAsync(group,false,ct));
   Check(group.IsWorking&&group.UploadingPhotoIds.Count==1,"Query snapshots uploaded references");
   var queryCopy=await VM.DuplicateAsync(group);Check(queryCopy.Photos.Count==group.Photos.Count,"Query permits independent group copy");
   var nonUpload=group.Photos.First(p=>!group.UploadingPhotoIds.Contains(p.Id));VM.Split(group,[nonUpload]);var duringSplit=VM.Selected!;
   Check(group.IsWorking&&duringSplit.Photos.Count==1,"Query permits splitting nonuploaded photo");
   delayed.Release.TrySetResult();await querying;Check(group.Confirmed.Brand=="Example" && duringSplit.Confirmed.Brand.Length==0,"Out-of-order result stays with original group");
   var kept=group.FolderName;VM.Merge([duringSplit,group]);Check(VM.Selected==group&&group.FolderName==kept&&group.Confirmed.Brand=="Example","Merge retains earlier group's result and naming");
   await VM.FlushWritesAsync();File.WriteAllText(Path.Combine(directory,"v12-verification.json"),JsonSerializer.Serialize(new{Status="Passed",Checks=checks,RealApiCalls=0},StateStore.Json));
  }catch(Exception error){File.WriteAllText(Path.Combine(directory,"v12-verification.json"),JsonSerializer.Serialize(new{Status="Failed",Error=error.ToString(),Checks=checks},StateStore.Json));}
 }
 private sealed class V12DelayedRecognition:IRecognitionService
 {
  public TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public async Task<RecognitionResult> RecognizeAsync(IReadOnlyList<Photo> photos,AppSettings settings,bool again,CancellationToken ct) {await Release.Task.WaitAsync(ct);return new(){Identity=new(){Brand="Example",Series="Model",Variant="Model Sport",Generation="1st"}};}
  public Task<VerificationReport> VerifyAsync(IReadOnlyList<Photo> photos,AppSettings settings,CarIdentity candidate,string questions,CancellationToken ct)=>throw new InvalidOperationException("Manual verification must not call an API");
 }
}
#endif
