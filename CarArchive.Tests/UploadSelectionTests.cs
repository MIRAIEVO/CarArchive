using CarArchive.Core;
static class UploadSelectionTests
{
 public static void Register(Action<string,Func<Task>> test)
 {
  test("Single upload uses last viewed reference",()=>{var a=new Photo();var b=new Photo();var g=new PhotoGroup{Photos=[a,b],RepresentativeId=a.Id,LastViewedId=b.Id};if(UploadSelection.Select(g).Single().Id!=b.Id)throw new Exception("wrong photo");return Task.CompletedTask;});
  test("Multiple upload uses all and only nonexcluded crops in every mode",()=>{var a=new Photo();var b=new Photo{Crop=new(0,0,.5,.5)};var c=new Photo{Crop=new(.2,.2,.4,.4)};var d=new Photo{Crop=new(0,0,1,1),Excluded=true};foreach(var mode in Enum.GetValues<RecognitionMode>()){var g=new PhotoGroup{Photos=[a,b,c,d],LastViewedId=a.Id,UploadMultiple=true,Mode=mode};if(!UploadSelection.Select(g).Select(p=>p.Id).SequenceEqual(new[]{b.Id,c.Id}))throw new Exception("incorrect crop set");}return Task.CompletedTask;});
  test("Multiple without crops falls back to current whole image",()=>{var a=new Photo();var b=new Photo();var g=new PhotoGroup{Photos=[a,b],LastViewedId=b.Id,UploadMultiple=true};if(UploadSelection.Select(g).Single().Id!=b.Id)throw new Exception("fallback");return Task.CompletedTask;});
  test("Archive undo removes only newly created empty folders",async()=>{var root=Path.Combine(Path.GetTempPath(),"CarArchive-empty-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var file=Path.Combine(root,"source.jpg");await File.WriteAllTextAsync(file,"original");var service=new ArchiveService(new StateStore(Path.Combine(root,"Data")));var group=new PhotoGroup{Photos=[new(){Path=file}]};var job=await service.PlanAsync(group,root,"Brand","Model",default);await service.ExecuteAsync(job,default);var unrelated=Path.Combine(root,"Brand","unrelated.txt");await File.WriteAllTextAsync(unrelated,"keep");await service.UndoAsync(job,default);if(Directory.Exists(Path.Combine(root,"Brand","Model"))||!File.Exists(unrelated)||!File.Exists(file))throw new Exception("unsafe directory cleanup");});
 }
}
