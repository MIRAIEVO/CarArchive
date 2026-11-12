using System.Text.Json;
using CarArchive.Core;

namespace CarArchive.ViewModels;

public sealed partial class MainViewModel
{
    private string namingRoot="";
    private Task? namingLoad;
    private int namingLoadVersion;
    public string NamingGuideDirectory=>NamingGuideStore.DirectoryFor(DataDirectory,State.Settings.ArchiveRoot);
    public async Task PrepareNamingGuideAsync()
    {
        while(true) { var root=State.Settings.ArchiveRoot;await EnsureNamingGuideAsync();if(root.Equals(State.Settings.ArchiveRoot,StringComparison.OrdinalIgnoreCase)) return; }
    }
    public Task EnsureNamingGuideAsync(bool force=false,bool backup=false)
    {
        var root=State.Settings.ArchiveRoot;
        if(root.Length==0) { Naming.Guide=null;return Task.CompletedTask; }
        if(!force && namingLoad is not null && namingRoot.Equals(root,StringComparison.OrdinalIgnoreCase)) return namingLoad;
        var preceding=namingLoad;
        namingRoot=root;var version=++namingLoadVersion;
        var directory=NamingGuideStore.DirectoryFor(DataDirectory,root);
        var snapshot=new WorkspaceState { Settings=new() { ArchiveRoot=root },Names=State.Names.ToList(),BrandFolders=new(State.BrandFolders),Rules=State.Rules.ToList(),NamingSamples=JsonSerializer.Deserialize<List<NamingLearningSample>>(JsonSerializer.Serialize(State.NamingSamples,StateStore.Json),StateStore.Json)!,LocalNamingRules=JsonSerializer.Deserialize<List<LocalNamingRule>>(JsonSerializer.Serialize(State.LocalNamingRules,StateStore.Json),StateStore.Json)!,
            Groups=Groups.Select(group=>new PhotoGroup { Id=group.Id,Archived=group.Archived,FolderName=group.FolderName,TargetBrand=group.TargetBrand,Confirmed=group.Confirmed.Clone() }).ToList() };
        return namingLoad=LoadAsync();
        async Task LoadAsync()
        {
            try
            {
                if(preceding is not null) { try { await preceding; } catch { } }
                await FlushWritesAsync();
                var guide=await Task.Run(()=>
                {
                    NamingGuide? old=null;
                    try { old=NamingGuideStore.Load(directory,root); }
                    catch(Exception error) when(error is IOException or JsonException or InvalidDataException) { /* Preserve and rebuild below. */ }
                    if(!force && old is not null && (!Directory.Exists(root) || NamingGuideStore.IsFresh(old,snapshot,Store.Jobs()))) return old;
                    Directory.CreateDirectory(directory);
                    var marker=Path.Combine(directory,"upgrade-1.3.backed-up");
                    if(!File.Exists(marker)) { Database.BackupNamingData();File.WriteAllText(marker,DateTime.UtcNow.ToString("O")); }
                    var stamp=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
                    if(backup) File.WriteAllText(Path.Combine(directory,"naming-records-backup-"+stamp+".json"),JsonSerializer.Serialize(new { snapshot.Names,snapshot.Rules,snapshot.BrandFolders,snapshot.NamingSamples,snapshot.LocalNamingRules },StateStore.Json));
                    if(backup && File.Exists(Path.Combine(directory,"naming-guide.json"))) File.Copy(Path.Combine(directory,"naming-guide.json"),Path.Combine(directory,"naming-guide.backup-"+stamp+".json"));
                    if(backup) foreach(var pattern in new[]{"naming-records-backup-*.json","naming-guide.backup-*.json"}) foreach(var stale in Directory.EnumerateFiles(directory,pattern).OrderByDescending(File.GetLastWriteTimeUtc).Skip(5)) File.Delete(stale);
                    var built=NamingGuideBuilder.Build(root,snapshot,Store.Jobs(),shutdown.Token);
                    NamingGuideStore.Save(directory,built,snapshot);return built;
                },shutdown.Token);
                if(version==namingLoadVersion && State.Settings.ArchiveRoot.Equals(root,StringComparison.OrdinalIgnoreCase)) Naming.Guide=guide;
            }
            catch { if(version==namingLoadVersion) namingLoad=null;throw; }
        }
    }
}
