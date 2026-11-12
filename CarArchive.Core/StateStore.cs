using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CarArchive.Core;

public sealed class StateStore : IStateStore
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = false, PropertyNameCaseInsensitive = true };
    private readonly string connectionString;
    private readonly string directory;
    private readonly object gate = new();
    private readonly Dictionary<string,string> groupCache = [];
    private readonly Dictionary<string,string> assetCache = [];
    public StateStore(string directory)
    {
        this.directory=directory; Directory.CreateDirectory(directory);
        connectionString=new SqliteConnectionStringBuilder { DataSource=Path.Combine(directory,"cararchive.db") }.ToString();
        using var db=Open(); using var cmd=db.CreateCommand();
        cmd.CommandText="PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; CREATE TABLE IF NOT EXISTS workspace(id INTEGER PRIMARY KEY,json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS jobs(id TEXT PRIMARY KEY,json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS groups_v2(id TEXT PRIMARY KEY,sort_index INTEGER NOT NULL,json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS assets_v2(id TEXT PRIMARY KEY,json TEXT NOT NULL);";
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var db=new SqliteConnection(connectionString); db.Open(); return db; }
    public string BackupNamingData()
    {
        lock(gate)
        {
            var path=Path.Combine(directory,"cararchive.pre-naming-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".db");
            using var source=Open();using var target=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path,Pooling=false }.ToString());
            target.Open();source.BackupDatabase(target);return path;
        }
    }
    public WorkspaceState Load()
    {
        lock(gate)
        {
            using var db=Open(); using var cmd=db.CreateCommand(); cmd.CommandText="SELECT json FROM workspace WHERE id=1";
            var json=cmd.ExecuteScalar() as string;
            var state=json is null?new WorkspaceState { SchemaVersion=2, WorkspaceId=File.Exists(Path.Combine(directory,"workspace-id.txt"))?File.ReadAllText(Path.Combine(directory,"workspace-id.txt")).Trim():Guid.NewGuid().ToString("N") }:JsonSerializer.Deserialize<WorkspaceState>(json,Json) ?? throw new InvalidDataException("本地数据库无法读取。请保留文件并恢复备份。");
            // Additive 1.3 fields still share the v2 tables. Back up once before saving the upgraded header.
            using var oldHeader=json is null?null:JsonDocument.Parse(json);
            if(oldHeader is not null && !oldHeader.RootElement.TryGetProperty("NamingSamples",out _))
            {
                var backupPath=Path.Combine(directory,"cararchive.pre-1.3.db");
                if(!File.Exists(backupPath)) { using var backup=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=backupPath,Pooling=false }.ToString());backup.Open();db.BackupDatabase(backup); }
            }
            if(state.SchemaVersion<2)
            {
                using var backup=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=Path.Combine(directory,"cararchive.pre-v2-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".db"),Pooling=false }.ToString());
                backup.Open(); db.BackupDatabase(backup);
                state.Settings.Mode=RecognitionMode.FieldsOnly; state.SchemaVersion=2; NormalizeAssets(state); Save(state); return state;
            }
            state.Groups=[];
            cmd.CommandText="SELECT id,json FROM groups_v2 ORDER BY sort_index";
            using(var reader=cmd.ExecuteReader()) while(reader.Read()) { var text=reader.GetString(1); groupCache[reader.GetString(0)]=text; state.Groups.Add(JsonSerializer.Deserialize<PhotoGroup>(text,Json)!); }
            state.Assets=[];
            cmd.CommandText="SELECT id,json FROM assets_v2";
            using(var reader=cmd.ExecuteReader()) while(reader.Read()) { var text=reader.GetString(1); assetCache[reader.GetString(0)]=text; state.Assets.Add(JsonSerializer.Deserialize<ImageAsset>(text,Json)!); }
            NormalizeAssets(state);
            LocalNamingLearning.Reconcile(state);
            foreach(var group in state.Groups.Where(g=>g.IsWorking)) { group.TaskState=GroupTaskState.Failed; group.LastError="应用重启，任务已中断；请手动重试或恢复归档。"; }
            return state;
        }
    }
    public static void NormalizeAssets(WorkspaceState state)
    {
        var byId=state.Assets.ToDictionary(a=>a.Id);
        var byPath=state.Assets.Where(a=>a.OriginalPath.Length>0).GroupBy(a=>a.OriginalPath,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
        foreach(var p in state.Groups.SelectMany(g=>g.Photos))
        {
            ImageAsset? asset=null;
            if(p.AssetId.Length>0) byId.TryGetValue(p.AssetId,out asset);
            var original=p.OriginalPath.Length>0?p.OriginalPath:p.Path;
            if(asset is null) byPath.TryGetValue(original,out asset);
            if(asset is null)
            {
                asset=new() { OriginalPath=original,PreviewPath=p.PreviewPath,ThumbnailPath=p.ThumbnailPath,CapturedAt=p.CapturedAt,Error=p.Error };
                var current=File.Exists(original)?original:p.Path;
                if(File.Exists(current)) { var info=new FileInfo(current); asset.Length=info.Length; asset.ModifiedTicks=info.LastWriteTimeUtc.Ticks; }
                state.Assets.Add(asset); byId[asset.Id]=asset; byPath[original]=asset;
            }
            p.Attach(asset);
        }
    }
    private static string Header(WorkspaceState state)=>JsonSerializer.Serialize(new { SchemaVersion=2,state.WorkspaceId,state.NamingSamples,state.LocalNamingRules,state.Settings,state.Rules,state.Names,state.BrandFolders,state.BatchCalls,state.BatchId },Json);
    public Action CaptureSave(WorkspaceState state)
    {
        var header=Header(state);
        var groups=state.Groups.Select((g,i)=>(g.Id,Index:i,Json:JsonSerializer.Serialize(g,Json))).ToList();
        var assets=state.Assets.Select(a=>(a.Id,Json:JsonSerializer.Serialize(a,Json))).ToList();
        return ()=> {
            lock(gate)
            {
                using var db=Open(); using var transaction=db.BeginTransaction();
                Execute(db,transaction,"INSERT INTO workspace VALUES(1,$json) ON CONFLICT(id) DO UPDATE SET json=$json",header);
                var ids=groups.Select(g=>g.Id).ToHashSet();
                using(var cmd=db.CreateCommand()) { cmd.Transaction=transaction; cmd.CommandText="SELECT id FROM groups_v2"; using var reader=cmd.ExecuteReader(); var removed=new List<string>(); while(reader.Read()) if(!ids.Contains(reader.GetString(0))) removed.Add(reader.GetString(0)); reader.Close(); foreach(var id in removed) { Execute(db,transaction,"DELETE FROM groups_v2 WHERE id=$id","",id); groupCache.Remove(id); } }
                foreach(var g in groups)
                {
                    if(!groupCache.TryGetValue(g.Id,out var old) || old!=g.Json) Execute(db,transaction,"INSERT INTO groups_v2 VALUES($id,$index,$json) ON CONFLICT(id) DO UPDATE SET json=$json,sort_index=$index",g.Json,g.Id,g.Index);
                    else Execute(db,transaction,"UPDATE groups_v2 SET sort_index=$index WHERE id=$id","",g.Id,g.Index);
                }
                foreach(var a in assets) if(!assetCache.TryGetValue(a.Id,out var old) || old!=a.Json) Execute(db,transaction,"INSERT INTO assets_v2 VALUES($id,$json) ON CONFLICT(id) DO UPDATE SET json=$json",a.Json,a.Id);
                transaction.Commit();
                foreach(var g in groups) groupCache[g.Id]=g.Json; foreach(var a in assets) assetCache[a.Id]=a.Json;
            }
        };
    }
    public Action CaptureGroup(PhotoGroup group)
    {
        var id=group.Id; var json=JsonSerializer.Serialize(group,Json);
        return ()=> { lock(gate) { using var db=Open(); Execute(db,null,"INSERT INTO groups_v2 VALUES($id,(SELECT COALESCE(MAX(sort_index),-1)+1 FROM groups_v2),$json) ON CONFLICT(id) DO UPDATE SET json=$json",json,id); groupCache[id]=json; } };
    }
    public Action CaptureAsset(ImageAsset asset)
    {
        var id=asset.Id; var json=JsonSerializer.Serialize(asset,Json);
        return ()=> { lock(gate) { using var db=Open(); Execute(db,null,"INSERT INTO assets_v2 VALUES($id,$json) ON CONFLICT(id) DO UPDATE SET json=$json",json,id); assetCache[id]=json; } };
    }
    public Action CaptureHeader(WorkspaceState state)
    {
        var json=Header(state);
        return ()=> { lock(gate) { using var db=Open(); Execute(db,null,"INSERT INTO workspace VALUES(1,$json) ON CONFLICT(id) DO UPDATE SET json=$json",json); } };
    }
    public void Save(WorkspaceState state)=>CaptureSave(state)();
    public void SaveGroup(PhotoGroup group)=>CaptureGroup(group)();
    public void SaveAsset(ImageAsset asset)=>CaptureAsset(asset)();
    public void SaveJob(ArchiveJob job)
    {
        var json=JsonSerializer.Serialize(job,Json);
        lock(gate) { using var db=Open(); Execute(db,null,"INSERT INTO jobs VALUES($id,$json) ON CONFLICT(id) DO UPDATE SET json=$json",json,job.Id); }
    }
    private static void Execute(SqliteConnection db,SqliteTransaction? transaction,string sql,string json,string? id=null,int index=0)
    {
        using var cmd=db.CreateCommand(); cmd.Transaction=transaction; cmd.CommandText=sql;
        cmd.Parameters.AddWithValue("$json",json); if(id is not null) cmd.Parameters.AddWithValue("$id",id); cmd.Parameters.AddWithValue("$index",index); cmd.ExecuteNonQuery();
    }
    public List<ArchiveJob> Jobs()
    {
        lock(gate) { using var db=Open(); using var cmd=db.CreateCommand(); cmd.CommandText="SELECT json FROM jobs"; using var reader=cmd.ExecuteReader(); var result=new List<ArchiveJob>(); while(reader.Read()) result.Add(JsonSerializer.Deserialize<ArchiveJob>(reader.GetString(0),Json)!); return result.OrderByDescending(j=>j.CreatedAt).ToList(); }
    }
}
