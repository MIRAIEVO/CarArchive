using System.Text.Json;
using CarArchive.Core;
using Microsoft.Data.Sqlite;

namespace CarArchive.Tools;

internal sealed record WorkspaceSnapshot(WorkspaceState State, List<ArchiveJob> Jobs, List<NamingClickLog> NamingClicks);

internal static class ReadOnlyWorkspaceSnapshot
{
    public static WorkspaceSnapshot Load(string directory)
    {
        var database = Path.Combine(Path.GetFullPath(directory), "cararchive.db");
        if (!File.Exists(database)) throw new FileNotFoundException("The workspace database does not exist.", database);
        using var connection = Open(database);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var reader = command.ExecuteReader()) while (reader.Read()) tables.Add(reader.GetString(0));
        if (!tables.Contains("workspace")) throw new InvalidDataException("The workspace table is missing.");
        command.CommandText = "SELECT json FROM workspace WHERE id=1";
        var json = command.ExecuteScalar() as string;
        var state = json is null ? new WorkspaceState() : JsonSerializer.Deserialize<WorkspaceState>(json, StateStore.Json)
            ?? throw new InvalidDataException("The workspace header is empty.");
        if (state.SchemaVersion >= 2 && tables.Contains("groups_v2"))
        {
            state.Groups = ReadRows<PhotoGroup>(connection, transaction, "SELECT json FROM groups_v2 ORDER BY sort_index");
            if (tables.Contains("assets_v2")) state.Assets = ReadRows<ImageAsset>(connection, transaction, "SELECT json FROM assets_v2");
        }
        // Attach references in memory only. Do not run migrations, backups or task reconciliation.
        var jobs = tables.Contains("jobs") ? ReadRows<ArchiveJob>(connection, transaction, "SELECT json FROM jobs") : [];
        var namingClicks = tables.Contains("naming_click_log")
            ? ReadRows<NamingClickLog>(connection, transaction, "SELECT json FROM naming_click_log ORDER BY id") : [];
        transaction.Commit();
        StateStore.NormalizeAssets(state);
        return new(state, jobs, namingClicks);
    }

    public static void Backup(string directory, string destination)
    {
        using var source = Open(Path.Combine(Path.GetFullPath(directory), "cararchive.db"));
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
        target.Open(); source.BackupDatabase(target);
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private, Pooling = false
        }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private static List<T> ReadRows<T>(SqliteConnection connection, SqliteTransaction transaction, string query)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = query;
        using var reader = command.ExecuteReader(); var rows = new List<T>();
        while (reader.Read()) rows.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), StateStore.Json)
            ?? throw new InvalidDataException("A workspace row is empty."));
        return rows;
    }
}
