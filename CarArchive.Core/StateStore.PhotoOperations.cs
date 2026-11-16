using System.Text.Json;

namespace CarArchive.Core;

public sealed partial class StateStore
{
    public void SavePhotoOperation(PhotoFileOperation operation)
    {
        var json=JsonSerializer.Serialize(operation,Json);
        lock(gate)
        {
            using var db=Open();using var command=db.CreateCommand();
            command.CommandText="CREATE TABLE IF NOT EXISTS photo_operations(id TEXT PRIMARY KEY,json TEXT NOT NULL); INSERT INTO photo_operations VALUES($id,$json) ON CONFLICT(id) DO UPDATE SET json=$json";
            command.Parameters.AddWithValue("$id",operation.Id);command.Parameters.AddWithValue("$json",json);command.ExecuteNonQuery();
        }
    }
    public List<PhotoFileOperation> PhotoOperations()
    {
        lock(gate)
        {
            using var db=Open();using var command=db.CreateCommand();command.CommandText="SELECT name FROM sqlite_master WHERE type='table' AND name='photo_operations'";
            if(command.ExecuteScalar() is null) return [];
            command.CommandText="SELECT json FROM photo_operations";using var reader=command.ExecuteReader();var result=new List<PhotoFileOperation>();
            while(reader.Read()) result.Add(JsonSerializer.Deserialize<PhotoFileOperation>(reader.GetString(0),Json) ?? throw new InvalidDataException("照片操作记录无法读取。"));
            return result.OrderBy(o=>o.CreatedAt).ToList();
        }
    }
    public void BackupPhotoLifecycleOnce()
    {
        lock(gate)
        {
            var path=Path.Combine(directory,"cararchive.pre-1.1.5-photos.db");if(File.Exists(path)) return;
            using var source=Open();using var backup=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource=path,Pooling=false }.ToString());
            backup.Open();source.BackupDatabase(backup);
        }
    }
}
