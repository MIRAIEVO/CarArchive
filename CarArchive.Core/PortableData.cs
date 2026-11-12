using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace CarArchive.Core;

public sealed record PortableDataSource(string Path, DateTime LastUsed)
{
    public override string ToString() => Path;
}

public static class PortableData
{
    public static string ResolveDirectory(string? executableDirectory=null)
    {
        var overridden=Environment.GetEnvironmentVariable("CARARCHIVE_DATA_DIR");
        if(!string.IsNullOrWhiteSpace(overridden)) return System.IO.Path.GetFullPath(overridden);
        var directory=System.IO.Path.GetFullPath(executableDirectory ?? AppContext.BaseDirectory).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var root=System.IO.Path.GetFileName(directory).Equals("Program",StringComparison.OrdinalIgnoreCase)?System.IO.Path.GetDirectoryName(directory)!:directory;
        return System.IO.Path.Combine(root,"Data");
    }
    public static void EnsureWritable(string directory)
    {
        Directory.CreateDirectory(directory);
        var probe=System.IO.Path.Combine(directory,".write-probe-"+Guid.NewGuid().ToString("N"));
        try { using var stream=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose);stream.WriteByte(0); }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { throw new IOException("程序数据目录不可写，请将完整便携文件夹移动至可写位置："+directory,e); }
    }
    public static IReadOnlyList<PortableDataSource> DiscoverSources(string destination)
    {
        var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var legacy=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CarArchive");
        void Add(string path) { if(File.Exists(System.IO.Path.Combine(path,"cararchive.db")) && !System.IO.Path.GetFullPath(path).Equals(System.IO.Path.GetFullPath(destination),StringComparison.OrdinalIgnoreCase)) found.Add(path); }
        Add(legacy);
        var parent=Directory.GetParent(System.IO.Path.GetDirectoryName(destination)!)?.FullName;
        if(parent is not null && Directory.Exists(parent))
            foreach(var folder in Directory.EnumerateDirectories(parent,"CarArchive-*")) Add(System.IO.Path.Combine(folder,"Data"));
        return found.Select(p=>new PortableDataSource(p,File.GetLastWriteTime(System.IO.Path.Combine(p,"cararchive.db"))))
            .OrderByDescending(s=>s.LastUsed).ToList();
    }
    public static string EnsureWorkspaceId(string directory)
    {
        var database=System.IO.Path.Combine(directory,"cararchive.db");
        var sidecar=System.IO.Path.Combine(directory,"workspace-id.txt");
        string? id=null;
        if(File.Exists(database))
        {
            using var db=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=database }.ToString());db.Open();
            using var command=db.CreateCommand();command.CommandText="SELECT json FROM workspace WHERE id=1";
            if(command.ExecuteScalar() is string json)
            {
                var node=JsonNode.Parse(json)!.AsObject();id=node["WorkspaceId"]?.GetValue<string>();
                if(!Guid.TryParse(id,out _))
                {
                    id=File.Exists(sidecar)?File.ReadAllText(sidecar).Trim():LegacyId(directory);
                    using(var backup=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=System.IO.Path.Combine(directory,"cararchive.pre-portable-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".db"),Pooling=false }.ToString())) { backup.Open();db.BackupDatabase(backup); }
                    node["WorkspaceId"]=id;command.CommandText="UPDATE workspace SET json=$json WHERE id=1";command.Parameters.AddWithValue("$json",node.ToJsonString());command.ExecuteNonQuery();
                }
            }
        }
        if(!Guid.TryParse(id,out _)) id=File.Exists(sidecar)?File.ReadAllText(sidecar).Trim():Guid.NewGuid().ToString("N");
        if(!Guid.TryParse(id,out _)) throw new InvalidDataException("工作区标识无效，请保留数据并恢复备份。");
        File.WriteAllText(sidecar,id);return id;
    }
    internal static string LegacyId(string source) => new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(source).TrimEnd(System.IO.Path.DirectorySeparatorChar).ToUpperInvariant()))[..16]).ToString("N");
    public static async Task ImportAsync(string source,string destination,CancellationToken ct=default)
    {
        await Task.Run(()=>Import(source,destination,ct),ct);
    }
    private static void Import(string source,string destination,CancellationToken ct)
    {
        source=System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(source));destination=System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(destination));
        RejectReparseDirectories(source);RejectReparseDirectories(destination);
        if(source.Equals(destination,StringComparison.OrdinalIgnoreCase) || destination.StartsWith(source+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("请选择另一个版本的数据目录。");
        if(!File.Exists(System.IO.Path.Combine(source,"cararchive.db"))) throw new FileNotFoundException("选定目录中没有 cararchive.db。");
        if(File.GetAttributes(System.IO.Path.Combine(source,"cararchive.db")).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("迁移来源的数据库不能是链接。");
        if(Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()) throw new IOException("目标 Data 目录已有内容，不会覆盖或合并。请使用新的便携包。");
        EnsureWritable(System.IO.Path.GetDirectoryName(destination)!);
        var temporary=destination+".import-"+Guid.NewGuid().ToString("N");
        using var sourceLock=WorkspaceLease.AcquireLegacy(source);
        using var lineage=WorkspaceLease.AcquireIdentity(ReadWorkspaceId(source) ?? LegacyId(source));
        try
        {
            Directory.CreateDirectory(temporary);ct.ThrowIfCancellationRequested();
            var targetDb=System.IO.Path.Combine(temporary,"cararchive.db");
            using(var input=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=System.IO.Path.Combine(source,"cararchive.db"),Mode=SqliteOpenMode.ReadOnly }.ToString()))
            using(var output=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=targetDb }.ToString()))
            { input.Open();output.Open();input.BackupDatabase(output);using var check=output.CreateCommand();check.CommandText="PRAGMA integrity_check";if((string?)check.ExecuteScalar()!="ok") throw new InvalidDataException("数据库完整性检查失败。"); }
            foreach(var name in new[]{"cache","naming","crashes"})
            {
                var directory=System.IO.Path.Combine(source,name);if(!Directory.Exists(directory) || File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) continue;
                foreach(var file in Directory.EnumerateFiles(directory,"*",new EnumerationOptions { RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=false }))
                {
                    ct.ThrowIfCancellationRequested();
                    if(File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint) || System.IO.Path.GetFileName(file).Contains("backup",StringComparison.OrdinalIgnoreCase)) continue;
                    var relative=System.IO.Path.GetRelativePath(source,file);var target=System.IO.Path.Combine(temporary,relative);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);File.Copy(file,target);
                    using var inputFile=File.OpenRead(file);using var outputFile=File.OpenRead(target);
                    if(!SHA256.HashData(inputFile).SequenceEqual(SHA256.HashData(outputFile))) throw new IOException("迁移文件校验失败："+relative);
                }
            }
            var key=System.IO.Path.Combine(source,"gemini-key.bin");
            if(File.Exists(key))
            {
                if(File.GetAttributes(key).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("迁移来源的 Key 文件不能是链接。");
                var target=System.IO.Path.Combine(temporary,"gemini-key.bin");File.Copy(key,target);
                using var input=File.OpenRead(key);using var output=File.OpenRead(target);
                if(!SHA256.HashData(input).SequenceEqual(SHA256.HashData(output))) throw new IOException("API Key 复制校验失败。");
            }
            RebaseDatabase(targetDb,source,destination,ReadWorkspaceId(source) ?? LegacyId(source));
            File.WriteAllText(System.IO.Path.Combine(temporary,"workspace-id.txt"),ReadWorkspaceId(source) ?? LegacyId(source));
            File.WriteAllText(System.IO.Path.Combine(temporary,"migration.json"),System.Text.Json.JsonSerializer.Serialize(new { Source=source,CompletedUtc=DateTime.UtcNow }));
            ct.ThrowIfCancellationRequested();
            SqliteConnection.ClearAllPools();
            if(Directory.Exists(destination)) Directory.Delete(destination);Directory.Move(temporary,destination);
        }
        finally { if(Directory.Exists(temporary)) Directory.Delete(temporary,true); }
    }
    private static void RejectReparseDirectories(string directory)
    {
        for(var current=new DirectoryInfo(directory);current is not null;current=current.Parent)
            if(current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("迁移目录不能经过符号链接或目录联接，请选择实际数据目录。");
    }
    private static string? ReadWorkspaceId(string directory)
    {
        var file=System.IO.Path.Combine(directory,"cararchive.db");if(!File.Exists(file)) return null;
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=file,Mode=SqliteOpenMode.ReadOnly }.ToString());db.Open();
        using var cmd=db.CreateCommand();cmd.CommandText="SELECT json FROM workspace WHERE id=1";
        if(cmd.ExecuteScalar() is not string json) return null;
        var node=JsonNode.Parse(json)!.AsObject();var id=node["WorkspaceId"]?.GetValue<string>();return Guid.TryParse(id,out _)?id:null;
    }
    private static void RebaseDatabase(string database,string source,string destination,string id)
    {
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=database }.ToString());db.Open();using var transaction=db.BeginTransaction();
        foreach(var table in new[]{"workspace","groups_v2","assets_v2"})
        {
            using var exists=db.CreateCommand();exists.Transaction=transaction;exists.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table";exists.Parameters.AddWithValue("$table",table);if(Convert.ToInt32(exists.ExecuteScalar())==0) continue;
            using var read=db.CreateCommand();read.Transaction=transaction;read.CommandText=$"SELECT id,json FROM {table}";
            var rows=new List<(object Id,string Json)>();using(var reader=read.ExecuteReader()) while(reader.Read()) rows.Add((reader.GetValue(0),reader.GetString(1)));
            foreach(var row in rows)
            {
                var node=JsonNode.Parse(row.Json)!;Rebase(node,source,destination);
                if(table=="workspace") node["WorkspaceId"]=id;
                using var write=db.CreateCommand();write.Transaction=transaction;write.CommandText=$"UPDATE {table} SET json=$json WHERE id=$id";write.Parameters.AddWithValue("$json",node.ToJsonString());write.Parameters.AddWithValue("$id",row.Id);write.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }
    private static void Rebase(JsonNode node,string source,string destination)
    {
        if(node is JsonObject obj) foreach(var item in obj.ToList())
        {
            if(item.Key is "PreviewPath" or "ThumbnailPath" && item.Value is JsonValue v && v.TryGetValue<string>(out var path) && path.StartsWith(source+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) obj[item.Key]=System.IO.Path.Combine(destination,System.IO.Path.GetRelativePath(source,path));
            else if(item.Value is not null) Rebase(item.Value,source,destination);
        }
        else if(node is JsonArray array) foreach(var item in array) if(item is not null) Rebase(item,source,destination);
    }
}
