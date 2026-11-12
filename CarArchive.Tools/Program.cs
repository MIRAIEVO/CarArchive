using System.Diagnostics;
using System.Text.Json;
using CarArchive.Core;

Console.OutputEncoding=System.Text.Encoding.UTF8;
if(args.Length!=2 || args[0] is not ("build-guide" or "benchmark-naming" or "benchmark-search")) { Console.Error.WriteLine("Usage: CarArchive.Tools <build-guide|benchmark-naming|benchmark-search> <app-data-directory>");return 2; }
var clock=Stopwatch.StartNew();var dataDirectory=Path.GetFullPath(args[1]);
var store=new StateStore(dataDirectory);var state=store.Load();
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
    var benchmarkGuide=NamingGuideBuilder.Build(state.Settings.ArchiveRoot,state,store.Jobs());
    var samples=benchmarkGuide.Inventory.Select(e=>new CarIdentity { Brand=e.Brand,Series=e.Family,Variant=e.Model,Code=e.RawCode,Generation=e.Generation,Phase=e.Phase,Package=e.Package,ProductionYears=e.ProductionYears }).ToArray();
    foreach(var car in samples) NamingGuideEngine.Generate(car,state,benchmarkGuide);
    var allocated=GC.GetAllocatedBytesForCurrentThread();clock.Restart();
    for(var repeat=0;repeat<3;repeat++) foreach(var car in samples) NamingGuideEngine.Generate(car,state,benchmarkGuide);
    Console.WriteLine(JsonSerializer.Serialize(new { Samples=samples.Length,Decisions=samples.Length*3,ElapsedMilliseconds=clock.Elapsed.TotalMilliseconds,AllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated }));
    return 0;
}
var backup=store.BackupNamingData();var output=NamingGuideStore.DirectoryFor(dataDirectory,state.Settings.ArchiveRoot);Directory.CreateDirectory(output);
var stamp=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
File.WriteAllText(Path.Combine(output,"naming-records-backup-"+stamp+".json"),JsonSerializer.Serialize(new { state.Names,state.Rules,state.BrandFolders },StateStore.Json));
if(File.Exists(Path.Combine(output,"naming-guide.json"))) File.Copy(Path.Combine(output,"naming-guide.json"),Path.Combine(output,"naming-guide.backup-"+stamp+".json"));
var guide=NamingGuideBuilder.Build(state.Settings.ArchiveRoot,state,store.Jobs());NamingGuideStore.Save(output,guide,state);
var loaded=NamingGuideStore.Load(output,state.Settings.ArchiveRoot)!;
if(loaded.Inventory.Count!=Directory.EnumerateDirectories(state.Settings.ArchiveRoot).Sum(path=>Directory.EnumerateDirectories(path).Count())) throw new Exception("Directory coverage is incomplete.");
Console.WriteLine(JsonSerializer.Serialize(new { Root=guide.SourceRoot,Endpoints=guide.Inventory.Count,Profiles=guide.Profiles.Count,Notes=guide.Notes.Select(n=>n.Path),Output=output,Backup=backup,
    Warnings=guide.Warnings,HistoricalWarnings=guide.Inventory.Count(e=>e.Warning.Length>0),IdentityMatched=guide.Inventory.Count(e=>e.HasIdentity),ElapsedSeconds=clock.Elapsed.TotalSeconds },new JsonSerializerOptions { WriteIndented=true }));
return 0;
