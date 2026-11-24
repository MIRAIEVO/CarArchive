using System.Diagnostics;
using System.Text.Json;
using CarArchive.Core;
using CarArchive.Tools;

Console.OutputEncoding=System.Text.Encoding.UTF8;
if(args is ["probe-guide",var guidePath,var probeOutput]) {
 var probeGuide=JsonSerializer.Deserialize<NamingGuide>(File.ReadAllText(guidePath),StateStore.Json)!;
 var probeNamer=new NamingService {Guide=probeGuide};var probeState=new WorkspaceState();
 var probeRows=new List<object>();
 foreach(var entry in probeGuide.Inventory) {
  var car=new CarIdentity {Brand=entry.Brand,Series=entry.Family,Variant=entry.Model,Code=entry.RawCode.Length>0?entry.RawCode:entry.Code,Generation=entry.Generation,Phase=entry.Phase,Package=entry.Package,ProductionYears=entry.ProductionYears};
  var variants=new[]{entry.Model,entry.Model.Replace("Cabriolet","Convertible").Replace("Station Wagon","Estate"),entry.Model.Replace("Sedan","Saloon")}.Distinct().ToArray();
  foreach(var variant in variants) {car.Variant=variant;var result=probeNamer.Suggest(car,probeState,[]);probeRows.Add(new {entry.Brand,entry.Family,Original=entry.Folder,Identity=car.Clone(),Default=NamingService.DefaultName(car),Generated=result.Name,Rule=result.Reason,Matches=result.Name==entry.Folder});}
 }
 Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(probeOutput))!);File.WriteAllText(probeOutput,JsonSerializer.Serialize(probeRows,StateStore.Json));Console.WriteLine(JsonSerializer.Serialize(new {Entries=probeGuide.Inventory.Count,Probes=probeRows.Count}));return 0;
}
if(args is ["refresh-shipped",var archiveRoot,var sourceData,var resources]) {
 var source=ReadOnlyWorkspaceSnapshot.Load(sourceData);source.State.Settings.ArchiveRoot=archiveRoot;
 var built=NamingGuideBuilder.Build(archiveRoot,source.State,source.Jobs);var portable=BuiltInNamingGuide.PortableSnapshot(built);
 var actual=Directory.EnumerateDirectories(archiveRoot).SelectMany(Directory.EnumerateDirectories).Count();
 if(portable.Inventory.Count!=actual)throw new InvalidDataException("Directory coverage is incomplete.");
 var noteCount=Directory.EnumerateDirectories(archiveRoot).Append(archiveRoot)
    .SelectMany(path=>Directory.EnumerateFiles(path,"*.txt")).Count(path=>Path.GetFileName(path).Contains("补充命名",StringComparison.Ordinal));
 if(portable.Notes.Count!=noteCount)throw new InvalidDataException("Naming supplement coverage is incomplete.");
 Directory.CreateDirectory(resources);NamingGuideStore.Save(resources,portable,new WorkspaceState());
 string[] headings=["品牌","车系","现有分类","推演完整型号","代次","代号","改款","默认候选名","指南生成名","完全匹配原名","采用规则"];
 var probes=new List<string[]> {headings};
 var review=new List<string[]> {new[]{"品牌","车系","现有分类","新名称","原因"}};
 var cleanState=new WorkspaceState();
 foreach(var entry in portable.Inventory)
 {
    var identity=new CarIdentity {Brand=entry.Brand,Series=entry.Family,Variant=entry.Model,Generation=entry.Generation,
        Code=entry.Code.Length>0?entry.Code:entry.RawCode,Phase=entry.Phase,Package=entry.Package,ProductionYears=entry.ProductionYears};
    var decision=NamingGuideEngine.Generate(identity,cleanState,portable);
    var name=NamingService.CleanGeneratedName(decision.Name);
    var match=name==entry.Folder;
    probes.Add([entry.Brand,entry.Family,entry.Folder,identity.Variant,identity.Generation,identity.Code,identity.Phase,
        NamingService.DefaultName(identity),name,match?"是":"否",decision.Reason]);
    if(!match)review.Add([entry.Brand,entry.Family,entry.Folder,name,decision.Reason]);
 }
 string Csv(IEnumerable<string[]> rows)=>string.Join("\r\n",rows.Select(row=>string.Join(",",row.Select(value=>
     "\""+(value.Length>0 && "=+-@".Contains(value[0])?"'":"")+value.Replace("\"","\"\"")+"\""))))+"\r\n";
 NamingGuideStore.WriteAtomic(Path.Combine(resources,"命名输入推演.csv"),Csv(probes),true);
 NamingGuideStore.WriteAtomic(Path.Combine(resources,"命名差异与待复核.csv"),Csv(review),true);
 NamingGuideStore.WriteAtomic(Path.Combine(resources,"命名学习覆盖.json"),JsonSerializer.Serialize(new{
    Version="1.3.3",Brands=portable.Inventory.Select(e=>e.Brand).Distinct().Count(),Folders=actual,FamilyProfiles=portable.Profiles.Count,
    SupplementFiles=noteCount,Probes=probes.Count-1,ExactProbeMatches=probes.Count-review.Count,DifferencesForReview=review.Count-1,
    IdentityMatched=portable.Inventory.Count(e=>e.HasIdentity),
    Scope="Local names and supplements; historic manufacturer evidence tables retained without claiming fresh verification."
 },new JsonSerializerOptions {WriteIndented=true}),false);
 Console.WriteLine(JsonSerializer.Serialize(new {Folders=portable.Inventory.Count,Brands=portable.Inventory.Select(e=>e.Brand).Distinct().Count(),Rules=portable.Profiles.Count,Notes=portable.Notes.Count,Warnings=portable.Warnings.Count}));return 0;
}
if(args is ["export-shipped",var data,var destination]) {
 var exportSnapshot=ReadOnlyWorkspaceSnapshot.Load(data);
 var built=NamingGuideBuilder.Build(exportSnapshot.State.Settings.ArchiveRoot,exportSnapshot.State,exportSnapshot.Jobs);
 var portable=BuiltInNamingGuide.PortableSnapshot(built);
 Directory.CreateDirectory(destination);
 NamingGuideStore.Save(destination,portable,exportSnapshot.State);
 Console.WriteLine($"Exported {portable.Inventory.Count} folders, {portable.Notes.Count} notes");return 0;
}
if(args is ["self-test-readonly"]) return ReadOnlyWorkspaceSelfTest.Run();
if(args.Length!=2 || args[0] is not ("build-guide" or "benchmark-naming" or "benchmark-search")) { Console.Error.WriteLine("Usage: CarArchive.Tools <build-guide|benchmark-naming|benchmark-search> <app-data-directory> | self-test-readonly");return 2; }
var clock=Stopwatch.StartNew();var dataDirectory=Path.GetFullPath(args[1]);
var snapshot=ReadOnlyWorkspaceSnapshot.Load(dataDirectory);var state=snapshot.State;
if(state.Settings.ArchiveRoot.Length==0 || !Directory.Exists(state.Settings.ArchiveRoot)) throw new DirectoryNotFoundException("The configured archive root must be accessible.");
if(args[0]=="benchmark-search")
{
    var searchGuide=NamingGuideStore.Load(NamingGuideStore.DirectoryFor(dataDirectory,state.Settings.ArchiveRoot),state.Settings.ArchiveRoot);
    clock.Restart();var search=new CategorySearchIndex(CategoryCatalogService.Scan(state.Settings.ArchiveRoot).Items,searchGuide);var indexMilliseconds=clock.Elapsed.TotalMilliseconds;
    string[] queries=["ghost ewb","A4 avnat b9","6 series lci","911 carrera 992","benz s580 w223",""];
    foreach(var query in queries) search.Match(query,"");
    var times=new List<double>();
    for(var repeat=0;repeat<10;repeat++) foreach(var query in queries) { clock.Restart();search.Match(query,"");times.Add(clock.Elapsed.TotalMilliseconds); }
    Console.WriteLine(JsonSerializer.Serialize(new { Endpoints=searchGuide?.Inventory.Count,IndexMilliseconds=indexMilliseconds,Queries=times.Count,AverageMilliseconds=times.Average(),MaxMilliseconds=times.Max() }));return 0;
}
if(args[0]=="benchmark-naming")
{
    var benchmarkGuide=NamingGuideBuilder.Build(state.Settings.ArchiveRoot,state,snapshot.Jobs);
    var samples=benchmarkGuide.Inventory.Select(e=>new CarIdentity { Brand=e.Brand,Series=e.Family,Variant=e.Model,Code=e.RawCode,Generation=e.Generation,Phase=e.Phase,Package=e.Package,ProductionYears=e.ProductionYears }).ToArray();
    foreach(var car in samples) NamingGuideEngine.Generate(car,state,benchmarkGuide);
    var allocated=GC.GetAllocatedBytesForCurrentThread();clock.Restart();
    for(var repeat=0;repeat<3;repeat++) foreach(var car in samples) NamingGuideEngine.Generate(car,state,benchmarkGuide);
    Console.WriteLine(JsonSerializer.Serialize(new { Samples=samples.Length,Decisions=samples.Length*3,ElapsedMilliseconds=clock.Elapsed.TotalMilliseconds,AllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated }));
    return 0;
}
var backup=Path.Combine(dataDirectory,"cararchive.pre-naming-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".db");ReadOnlyWorkspaceSnapshot.Backup(dataDirectory,backup);var output=NamingGuideStore.DirectoryFor(dataDirectory,state.Settings.ArchiveRoot);Directory.CreateDirectory(output);
var stamp=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
File.WriteAllText(Path.Combine(output,"naming-records-backup-"+stamp+".json"),JsonSerializer.Serialize(new { state.Names,state.Rules,state.BrandFolders },StateStore.Json));
if(File.Exists(Path.Combine(output,"naming-guide.json"))) File.Copy(Path.Combine(output,"naming-guide.json"),Path.Combine(output,"naming-guide.backup-"+stamp+".json"));
var guide=NamingGuideBuilder.Build(state.Settings.ArchiveRoot,state,snapshot.Jobs);NamingGuideStore.Save(output,guide,state);
var loaded=NamingGuideStore.Load(output,state.Settings.ArchiveRoot)!;
if(loaded.Inventory.Count!=Directory.EnumerateDirectories(state.Settings.ArchiveRoot).Sum(path=>Directory.EnumerateDirectories(path).Count())) throw new Exception("Directory coverage is incomplete.");
Console.WriteLine(JsonSerializer.Serialize(new { Root=guide.SourceRoot,Endpoints=guide.Inventory.Count,Profiles=guide.Profiles.Count,Notes=guide.Notes.Select(n=>n.Path),Output=output,Backup=backup,
    Warnings=guide.Warnings,HistoricalWarnings=guide.Inventory.Count(e=>e.Warning.Length>0),IdentityMatched=guide.Inventory.Count(e=>e.HasIdentity),ElapsedSeconds=clock.Elapsed.TotalSeconds },new JsonSerializerOptions { WriteIndented=true }));
return 0;
