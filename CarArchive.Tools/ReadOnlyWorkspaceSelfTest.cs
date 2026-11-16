using System.Security.Cryptography;
using System.Text.Json;
using CarArchive.Core;
using Microsoft.Data.Sqlite;

namespace CarArchive.Tools;

internal static class ReadOnlyWorkspaceSelfTest
{
    public static int Run()
    {
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CarArchive-tools-readonly-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory); var passed = 0;
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); passed++; }
        try
        {
            var source = Path.Combine(directory, "current"); Directory.CreateDirectory(source);
            Create(source, 2);
            var database = Path.Combine(source, "cararchive.db"); var before = Hash(database);
            var paths = Directory.GetFileSystemEntries(source).Order(StringComparer.Ordinal).ToArray();
            var snapshot = ReadOnlyWorkspaceSnapshot.Load(source);
            Check(snapshot.State.Groups.Count == 1 && snapshot.State.Assets.Count == 1 && snapshot.Jobs.Single().Status == "Interrupted", "Current groups/assets/jobs load through read-only SQL");
            Check(snapshot.State.Groups.Single().TaskState == GroupTaskState.Running && snapshot.State.WorkspaceId == "readonly-test", "Read-only tools do not reconcile tasks or regenerate workspace identity");
            _ = ReadOnlyWorkspaceSnapshot.Load(source);
            Check(before == Hash(database) && paths.SequenceEqual(Directory.GetFileSystemEntries(source).Order(StringComparer.Ordinal)), "Repeated reads do not change database bytes or create backup/migration files");
            var missing = Path.Combine(directory, "missing");
            try { ReadOnlyWorkspaceSnapshot.Load(missing); throw new InvalidOperationException("Expected missing database failure"); }
            catch (FileNotFoundException) { Check(!Directory.Exists(missing), "Missing workspace is not created by inspection"); }
            var legacy = Path.Combine(directory, "legacy"); Directory.CreateDirectory(legacy); Create(legacy, 1);
            var legacyBefore = Hash(Path.Combine(legacy, "cararchive.db")); var old = ReadOnlyWorkspaceSnapshot.Load(legacy);
            Check(old.State.Groups.Single().Id == "sample" && legacyBefore == Hash(Path.Combine(legacy, "cararchive.db")), "Legacy JSON is inspected in memory without schema upgrade");
            Console.WriteLine($"{passed}/{passed} read-only tool checks passed."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            SqliteConnection.ClearAllPools();
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (directory.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(directory).StartsWith("CarArchive-tools-readonly-", StringComparison.Ordinal)
                && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static void Create(string directory, int version)
    {
        var group = new PhotoGroup { Id = "sample", TaskState = GroupTaskState.Running, Photos = [new() { AssetId = "asset", Path = "missing-original.jpg", OriginalPath = "missing-original.jpg" }] };
        var asset = new ImageAsset { Id = "asset", OriginalPath = "missing-original.jpg" };
        var header = new WorkspaceState { SchemaVersion = version, WorkspaceId = "readonly-test", Groups = [group], Assets = [asset] };
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "cararchive.db"), Pooling = false }.ToString()); db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "CREATE TABLE workspace(id INTEGER PRIMARY KEY,json TEXT NOT NULL); CREATE TABLE jobs(id TEXT PRIMARY KEY,json TEXT NOT NULL);"; command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO workspace VALUES(1,$json)"; command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(header, StateStore.Json)); command.ExecuteNonQuery(); command.Parameters.Clear();
        command.CommandText = "INSERT INTO jobs VALUES('job',$json)"; command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(new ArchiveJob { Id = "job", GroupId = group.Id, Status = "Interrupted" }, StateStore.Json)); command.ExecuteNonQuery(); command.Parameters.Clear();
        if (version < 2) return;
        command.CommandText = "CREATE TABLE groups_v2(id TEXT PRIMARY KEY,sort_index INTEGER NOT NULL,json TEXT NOT NULL); CREATE TABLE assets_v2(id TEXT PRIMARY KEY,json TEXT NOT NULL);"; command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO groups_v2 VALUES('sample',0,$json)"; command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(group, StateStore.Json)); command.ExecuteNonQuery(); command.Parameters.Clear();
        command.CommandText = "INSERT INTO assets_v2 VALUES('asset',$json)"; command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(asset, StateStore.Json)); command.ExecuteNonQuery();
    }
}
