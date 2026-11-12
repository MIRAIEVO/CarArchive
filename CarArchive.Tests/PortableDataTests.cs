using CarArchive.Core;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

static class PortableDataTests
{
    private static void Check(bool condition,string message="Portable data assertion failed") { if(!condition) throw new Exception(message); }
    public static void Register(Action<string,Func<Task>> test)
    {
        test("Portable: Program direct launch and root launcher resolve the same Data",()=>
        {
            var old=Environment.GetEnvironmentVariable("CARARCHIVE_DATA_DIR");Environment.SetEnvironmentVariable("CARARCHIVE_DATA_DIR",null);
            try { var root=Area();Check(PortableData.ResolveDirectory(root)==PortableData.ResolveDirectory(Path.Combine(root,"Program"))); }
            finally { Environment.SetEnvironmentVariable("CARARCHIVE_DATA_DIR",old); }return Task.CompletedTask;
        });
        test("Portable: import preserves originals, jobs, key and lineage while rebasing caches",async ()=>
        {
            var source=Area();var destination=Path.Combine(Area(),"release with spaces","Data");var originals=Area();var original=Path.Combine(originals,"test.jpg");File.WriteAllText(original,"original");
            Directory.CreateDirectory(Path.Combine(source,"cache"));File.WriteAllText(Path.Combine(source,"cache","thumb.jpg"),"thumbnail");
            Directory.CreateDirectory(Path.Combine(source,"naming","root"));File.WriteAllText(Path.Combine(source,"naming","root","naming-guide.json"),"{\"Version\":1}");File.WriteAllText(Path.Combine(source,"naming","root","naming-guide.backup-old.json"),"old backup");
            File.WriteAllBytes(Path.Combine(source,"gemini-key.bin"),[1,2,3]);File.WriteAllText(Path.Combine(source,"cararchive.pre-naming-old.db"),"old backup");
            var state=new WorkspaceState { SchemaVersion=2 };var asset=new ImageAsset { OriginalPath=original,ThumbnailPath=Path.Combine(source,"cache","thumb.jpg") };var photo=new Photo { Path=original,OriginalPath=original,ThumbnailPath=asset.ThumbnailPath };photo.Attach(asset);state.Assets.Add(asset);state.Groups.Add(new() { Photos=[photo],FolderName="Test (1st)" });
            var store=new StateStore(source);store.Save(state);store.SaveJob(new() { GroupId=state.Groups[0].Id,Status="Interrupted",Entries=[new() { Source=original,Destination=Path.Combine(originals,"archive.jpg") }] });
            SqliteConnection.ClearAllPools();var sourceHash=SHA256.HashData(File.ReadAllBytes(Path.Combine(source,"cararchive.db")));await PortableData.ImportAsync(source,destination);
            var loaded=new StateStore(destination).Load();Check(loaded.Groups.Single().Photos.Single().Path==original);Check(loaded.Assets.Single().ThumbnailPath==Path.Combine(destination,"cache","thumb.jpg"));Check(new StateStore(destination).Jobs().Count==1);Check(File.ReadAllBytes(Path.Combine(destination,"gemini-key.bin")).SequenceEqual(new byte[]{1,2,3}));Check(!Directory.GetFiles(destination,"*backup*",SearchOption.AllDirectories).Any());Check(!Directory.GetFiles(destination,"*pre-naming*",SearchOption.AllDirectories).Any());Check(SHA256.HashData(File.ReadAllBytes(Path.Combine(source,"cararchive.db"))).SequenceEqual(sourceHash));Check(File.ReadAllText(original)=="original");Check(loaded.WorkspaceId==state.WorkspaceId);
        });
        test("Portable: trailing directory separators preserve cache rebasing and source locks",async ()=>
        {
            var source=Area();var destination=Path.Combine(Area(),"Data");Directory.CreateDirectory(Path.Combine(source,"cache"));var cache=Path.Combine(source,"cache","thumb.jpg");File.WriteAllText(cache,"cache");
            var state=new WorkspaceState { SchemaVersion=2,Assets=[new() { OriginalPath="original.jpg",ThumbnailPath=cache }] };new StateStore(source).Save(state);
            await PortableData.ImportAsync(source+Path.DirectorySeparatorChar,destination+Path.DirectorySeparatorChar);
            Check(new StateStore(destination).Load().Assets.Single().ThumbnailPath==Path.Combine(destination,"cache","thumb.jpg"));
        });
        test("Portable: legacy additive upgrade creates one consistent backup only",()=>
        {
            var directory=Area();var store=new StateStore(directory);store.Save(new() { SchemaVersion=2 });
            using(var db=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=Path.Combine(directory,"cararchive.db") }.ToString()))
            { db.Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE workspace SET json='{\"SchemaVersion\":2}' WHERE id=1";cmd.ExecuteNonQuery(); }
            store.Load();var backup=Path.Combine(directory,"cararchive.pre-1.3.db");Check(File.Exists(backup));var hash=SHA256.HashData(File.ReadAllBytes(backup));store.Load();Check(SHA256.HashData(File.ReadAllBytes(backup)).SequenceEqual(hash));return Task.CompletedTask;
        });
        test("Portable: nonempty destination is never overwritten or merged",async ()=>
        {
            var source=Area();new StateStore(source).Save(new() { SchemaVersion=2 });var destination=Area();var sentinel=Path.Combine(destination,"keep.txt");File.WriteAllText(sentinel,"keep");await Throws<IOException>(()=>PortableData.ImportAsync(source,destination));Check(File.ReadAllText(sentinel)=="keep");
        });
        test("Portable: canceled import leaves source and destination untouched",async ()=>
        {
            var source=Area();new StateStore(source).Save(new() { SchemaVersion=2 });var destination=Path.Combine(Area(),"Data");using var ct=new CancellationTokenSource();ct.Cancel();await Throws<OperationCanceledException>(()=>PortableData.ImportAsync(source,destination,ct.Token));Check(!Directory.Exists(destination));Check(File.Exists(Path.Combine(source,"cararchive.db")));
        });
        test("Portable: two versions of the same workspace cannot run together",()=>
        {
            var first=Area();var second=Area();var state=new WorkspaceState { SchemaVersion=2 };new StateStore(first).Save(state);new StateStore(second).Save(state);using var lease=WorkspaceLease.Acquire(first);try { using var other=WorkspaceLease.Acquire(second);throw new Exception("Second workspace unexpectedly acquired"); }catch(IOException) { }return Task.CompletedTask;
        });
        test("Portable: import refuses an actively leased source",()=>
        {
            var source=Area();new StateStore(source).Save(new() { SchemaVersion=2 });using var lease=WorkspaceLease.Acquire(source);Throws<IOException>(()=>PortableData.ImportAsync(source,Path.Combine(Area(),"Data"))).GetAwaiter().GetResult();return Task.CompletedTask;
        });
        test("Portable: unwritable data path reports failure without a fallback",()=>
        {
            var file=Path.Combine(Area(),"file");File.WriteAllText(file,"occupied");try { PortableData.EnsureWritable(file);throw new Exception("Expected write failure"); }catch(IOException) { }return Task.CompletedTask;
        });
    }
    private static string Area() { var p=Path.Combine(Path.GetTempPath(),"CarArchive-portable-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p; }
    private static async Task Throws<T>(Func<Task> run) where T:Exception { try { await run(); }catch(T) { return; }throw new Exception("Expected "+typeof(T).Name); }
}
