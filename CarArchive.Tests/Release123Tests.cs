using CarArchive.Core;
public static class Release123Tests
{
 public static void Register(Action<string,Func<Task>> test)
 {
  test("123 release: chassis suffix preserves numbered facelifts rather than treating them as generation",()=>
  {
   var root=Path.Combine(Path.GetTempPath(),"CarArchive-phase-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Audi","A8 (D3 2nd Facelift)"));
   var guide=NamingGuideBuilder.Build(root,new());var entry=guide.Inventory.Single();
   if(entry.Phase!="2nd Facelift" || entry.Code!="D3")throw new Exception("Facelift ordinal lost");
   var name=NamingGuideEngine.Generate(new(){Brand="Audi",Series="A8",Variant="A8",Generation="2nd",Code="D3",Phase="2nd Facelift"},new(),guide).Name;
   if(name!="A8 (D3 2nd_Facelift)")throw new Exception(name);return Task.CompletedTask;
  });
  test("123 release: preview refresh does not re-notify unchanged thumbnail URI",()=>
  {
   var asset=new ImageAsset{ThumbnailPath=Path.Combine(Path.GetTempPath(),"thumbnail.jpg")};var photo=new Photo();photo.Attach(asset);var notifications=0;photo.PropertyChanged+=(_,args)=>{if(args.PropertyName==nameof(Photo.ThumbnailUri))notifications++;};
   asset.PreviewPath="full.png";asset.Refresh();asset.Error="";asset.Refresh();if(notifications!=0)throw new Exception("Unchanged thumbnail refreshed");asset.ThumbnailPath=Path.Combine(Path.GetTempPath(),"different.jpg");asset.Refresh();if(notifications!=1)throw new Exception("New thumbnail not published");return Task.CompletedTask;
  });
  test("123 release: model spelling preserves numeric distinctions and package words",()=>
  {
   if(LocalModelSpelling.Key("S 63 Coupe")!=LocalModelSpelling.Key("S63 Coupe") || LocalModelSpelling.Key("S63") == LocalModelSpelling.Key("S65") || LocalModelSpelling.Key("M Sport Package")==LocalModelSpelling.Key("M Sport"))throw new Exception("Model distinctions lost");return Task.CompletedTask;
  });
  test("123 release: required generation and per-generation facelift audit stay in generic prompt",()=>
  {
   var prompt=IdentityFieldContract.Instructions;if(!prompt.Contains("1st when only one")||!prompt.Contains("exactly one facelift")||!prompt.Contains("3rd Facelift")||prompt.Contains("2013"))throw new Exception("Field contract incomplete");
   if(RevisionEvidence.Warning(new(){Brand="Audi",Series="Q7",Variant="Q7",Generation="2nd",Phase="Facelift"}) is null || RevisionEvidence.Warning(new(){Brand="Audi",Series="Q7",Variant="Q7",Generation="2nd",Phase="2nd Facelift"}) is not null)throw new Exception("Known multiple facelift warning incorrect");return Task.CompletedTask;
  });
 }
}
