using System.Runtime.InteropServices;
using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace DuplicateChecker.Core;

public record MoveItem(MediaFile Source,string Destination,string? Hash=null,string State="Pending",string? Error=null,string? Against=null);
public record MoveManifest(string Target,string Comparison,MoveItem[] Items);
public record MoveSummary(int Done,int Pending,int Errors);

public static class MoveRun
{
    public static string Create(ScanResult scan)
    {
        Paths.Validate(scan.Target,scan.Comparison,scan.Output);
        var root=Path.Combine(scan.Output,$"查重结果-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        var items=scan.Files.Where(item=>item.Status!=Classification.Unknown).Select(item=>
            new MoveItem(item.File,Path.Combine(item.Status==Classification.Existing?"已有":"暂无",
                Path.GetRelativePath(scan.Target,item.File.Path)),item.Hash,Against:item.Against)).ToArray();
        if(items.Length==0)throw new InvalidOperationException("没有可移动的文件。");
        Directory.CreateDirectory(root);
        using(var marker=new FileStream(Path.Combine(root,Paths.Marker),FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {
            JsonSerializer.Serialize(marker,new MoveManifest(scan.Target,scan.Comparison,items));marker.Flush(true);
        }
        Directory.CreateDirectory(Path.Combine(root,"已有"));Directory.CreateDirectory(Path.Combine(root,"暂无"));
        return root;
    }
    public static MoveItem[] ReadEntries(string root)
    {
        root=Paths.Canonical(root);Paths.NoLinks(root);
        return ReadEntries(root,ReadManifest(root),out _);
    }

    public static Task<MoveItem[]> LoadAsync(string root,CancellationToken ct)=>Task.Run(()=>
    {
        root=Paths.Canonical(root);
        using var mutex=LockRun(root);
        var items=ReadEntries(root,ReadManifest(root),out _);
        var directory=Path.Combine(root,".operations");
        if(!Directory.Exists(directory))return items;
        var legacy=new List<string>();
        var temporary=new List<string>();
        foreach(var path in Directory.EnumerateFiles(directory))
        {
            var name=Path.GetFileName(path);
            var suffix=name.EndsWith(".json.new",StringComparison.OrdinalIgnoreCase)?".json.new":".json";
            if(!name.EndsWith(suffix,StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(name[..^suffix.Length],out var index) || index<0 || index>=items.Length)continue;
            Paths.NoLinks(path);
            (suffix==".json"?legacy:temporary).Add(path);
        }
        if(legacy.Count>0)MoveJournal.Consolidate(directory,items);
        foreach(var path in legacy.Concat(temporary))File.Delete(path);
        MoveJournal.RemoveTemporary(directory);
        return items;
    },ct);

    private static FileStream LockRun(string root)
    {
        Paths.NoLinks(root);
        if(!File.Exists(Path.Combine(root,Paths.Marker)))throw new IOException("所选文件夹没有移动记录。");
        var lockPath=Path.Combine(root,".operation.lock");
        Paths.NoLinks(lockPath);
        return new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose);
    }

    private static MoveManifest ReadManifest(string root)
    {
        var path=Path.Combine(root,Paths.Marker);Paths.NoLinks(path);
        return JsonSerializer.Deserialize<MoveManifest>(File.ReadAllText(path))??throw new IOException("无法读取移动清单。");
    }

    private static MoveItem[] ReadEntries(string root,MoveManifest manifest,out long journalLength)
    {
        var logDir=Path.Combine(root,".operations");Paths.NoLinks(logDir);
        var items=manifest.Items;
        void Apply(MoveJournal.Entry stored)
        {
            var index=stored.Index;
            if(index<0 || index>=items.Length || stored.Item.Source!=items[index].Source ||
                stored.Item.Destination!=items[index].Destination || stored.Item.Against!=items[index].Against)
                throw new IOException("移动日志与清单不一致。");
            items[index]=stored.Item;
        }
        if(Directory.Exists(logDir))
            foreach(var path in Directory.EnumerateFiles(logDir,"*.json"))
            {
                if(!int.TryParse(Path.GetFileNameWithoutExtension(path),out var index))continue;
                Paths.NoLinks(path);
                var stored=JsonSerializer.Deserialize<MoveJournal.Entry>(File.ReadAllBytes(path));
                if(stored is null || stored.Index!=index)throw new IOException("移动日志与清单不一致。");
                Apply(stored);
            }
        journalLength=MoveJournal.Replay(logDir,Apply);
        return items;
    }
    public static Task<MoveSummary> ExecuteAsync(string root,bool restore,IProgress<ScanProgress>? progress,CancellationToken ct)=>
        Task.Run(async()=>{
            root=Paths.Canonical(root);
            using var mutex=LockRun(root);
            var manifest=ReadManifest(root);
            if(Paths.Within(manifest.Comparison,root)||Paths.Within(manifest.Comparison,manifest.Target)||
                Paths.Within(manifest.Target,manifest.Comparison))throw new IOException("恢复清单中的路径相互重叠。");
            var items=ReadEntries(root,manifest,out var journalLength);
            var logDir=Directory.CreateDirectory(Path.Combine(root,".operations")).FullName;
            Paths.NoLinks(logDir);
            using var journal=new MoveJournal(logDir,journalLength);
            var completed=0;
            var stage=restore?"还原文件":"移动文件";
            progress?.Report(new(stage,0,items.Length));
            var parallel=new ParallelOptions{MaxDegreeOfParallelism=Math.Clamp(Environment.ProcessorCount,4,12),CancellationToken=ct};
            await Parallel.ForEachAsync(Enumerable.Range(0,items.Length),parallel,async (index,token)=>
            {
                token.ThrowIfCancellationRequested();
                var item=items[index];var saved=item;
                try
                {
                    var destination=Path.GetFullPath(Path.Combine(root,item.Destination));
                    if(!Paths.Within(root,destination)||!Paths.Within(manifest.Target,item.Source.Path)||
                        Paths.Within(root,item.Source.Path)||Paths.Within(manifest.Comparison,item.Source.Path)||
                        !(Paths.Within(Path.Combine(root,"已有"),destination)||Paths.Within(Path.Combine(root,"暂无"),destination)))
                        throw new IOException("清单路径超出允许范围。");
                    if(restore && item.State is "Pending" or "Restored" || !restore && item.State is "Moved" or "Restored")return;
                    if(!restore && item.State is "Restoring" or "RestorePublished")
                        throw new IOException("文件还原曾中断，请继续还原。");
                    var from=restore?destination:item.Source.Path;var to=restore?item.Source.Path:destination;
                    Paths.NoLinks(from);Paths.NoLinks(to);
                    string? hash=item.Hash;
                    if(item.State=="Pending")
                    {
                        item.Source.Verify();
                        var actual=await Scanner.FullHashAsync(item.Source,ct);
                        if(hash is not null && actual!=hash)throw new IOException("源文件与扫描内容不同。");
                        if(item.Against is not null)
                        {
                            if(!Paths.Within(manifest.Comparison,item.Against))throw new IOException("比较路径超出允许范围。");
                            Paths.NoLinks(item.Against);
                            if(await Scanner.FullHashAsync(MediaFile.Read(item.Against),ct)!=actual)throw new IOException("比较文件已变化，请重新检查。");
                        }
                        hash=actual;
                    }
                    if(hash is null)throw new IOException("缺少已校验的文件内容。");
                    if(restore && item.State=="Published")
                    {
                        var verified=MediaFile.Read(from);
                        if(await Scanner.FullHashAsync(verified,ct)!=hash)
                            throw new IOException("跨盘移动记录无法核实。");
                        if(File.Exists(to))
                        {
                            if(await Scanner.FullHashAsync(MediaFile.Read(to),ct)!=hash)
                                throw new IOException("原位置有冲突，保留两份。");
                            File.Delete(from);
                        }
                        else
                        {
                            saved=item with{State="Restoring",Error=null};await journal.SaveAsync(index,saved);
                            await MoveAsync(verified,to,hash,()=>{saved=saved with{State="RestorePublished"};return journal.SaveAsync(index,saved);},ct);
                        }
                    }
                    else if(item.State==(restore?"RestorePublished":"Published"))
                    {
                        if(!File.Exists(to)||await Scanner.FullHashAsync(MediaFile.Read(to),ct)!=hash)
                            throw new IOException("跨盘复制的目标内容发生变化。");
                        if(File.Exists(from))
                        {
                            if(await Scanner.FullHashAsync(MediaFile.Read(from),ct)!=hash)
                                throw new IOException("跨盘移动的源内容发生变化，保留两份。");
                            File.Delete(from);
                        }
                    }
                    else if(restore && item.State=="Moving" && !File.Exists(from))
                    {
                        if(!File.Exists(to)||await Scanner.FullHashAsync(MediaFile.Read(to),ct)!=hash)throw new IOException("原位置有冲突或文件缺失。");
                    }
                    else if(!File.Exists(from))
                    {
                        if(!File.Exists(to)||await Scanner.FullHashAsync(MediaFile.Read(to),ct)!=hash||
                            item.State!=(restore?"Restoring":"Moving"))throw new IOException("中断记录无法安全核实。");
                    }
                    else
                    {
                        if(File.Exists(to))throw new IOException("目标位置已有文件，保留两份。");
                        var verified=item.State=="Pending"?item.Source:MediaFile.Read(from);
                        if(item.State!="Pending" && await Scanner.FullHashAsync(verified,ct)!=hash)throw new IOException("文件已变化："+from);
                        saved=item with{Hash=hash,State=restore?"Restoring":"Moving",Error=null};await journal.SaveAsync(index,saved);
                        await MoveAsync(verified,to,hash,()=>{saved=saved with{State=restore?"RestorePublished":"Published"};return journal.SaveAsync(index,saved);},ct);
                    }
                    saved=item with{Hash=hash,State=restore?"Restored":"Moved",Error=null};await journal.SaveAsync(index,saved);items[index]=saved;
                }
                catch(Exception error) when(Scanner.IsIo(error)){saved=saved with{Error=error.Message};await journal.SaveAsync(index,saved);items[index]=saved;}
                finally
                {
                    var done=Interlocked.Increment(ref completed);
                    if(done%32==0||done==items.Length)progress?.Report(new(stage,done,items.Length));
                }
            });
            return new MoveSummary(items.Count(item=>item.State==(restore?"Restored":"Moved")),
                items.Count(item=>restore?item.State is "Moved" or "Moving" or "Restoring" or "Published" or "RestorePublished":item.State is not ("Moved" or "Restored")),
                items.Count(item=>item.Error is not null));
        },ct);

    [DllImport("kernel32.dll",EntryPoint="MoveFileExW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern bool Rename(string from,string to,int flags);

    private static async Task MoveAsync(MediaFile file,string to,string hash,Func<Task> published,CancellationToken ct)
    {
        var from=file.Path;
        ct.ThrowIfCancellationRequested();Paths.NoLinks(from);Paths.NoLinks(to);Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        file.Verify();
        if(Rename(from,to,0))return;
        if(Marshal.GetLastWin32Error()!=17)throw new IOException("无法移动文件："+from);
        var temporary=to+"."+Guid.NewGuid().ToString("N")+".partial";
        try
        {
            await CopyWithHashAsync(from,temporary,hash,ct);
            if(await Scanner.FullHashAsync(MediaFile.Read(temporary),ct)!=hash)
                throw new IOException("跨盘复制校验失败，源文件已保留。");
            file.Verify();
            File.SetLastWriteTimeUtc(temporary,new DateTime(file.Modified,DateTimeKind.Utc));
            File.SetCreationTimeUtc(temporary,new DateTime(file.Created,DateTimeKind.Utc));
            ct.ThrowIfCancellationRequested();File.Move(temporary,to,false);await published();
            file.Verify();File.Delete(from);
        }
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }

    private static async Task CopyWithHashAsync(string from,string temporary,string expected,CancellationToken ct)
    {
        const int size=128*1024;
        await using var input=new FileStream(from,FileMode.Open,FileAccess.Read,FileShare.Read,size,true);
        await using var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,size,true);
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer=ArrayPool<byte>.Shared.Rent(size);
        try
        {
            int read;
            while((read=await input.ReadAsync(buffer.AsMemory(0,size),ct))>0)
            {
                hash.AppendData(buffer,0,read);
                await output.WriteAsync(buffer.AsMemory(0,read),ct);
            }
            if(Convert.ToHexString(hash.GetHashAndReset())!=expected)
                throw new IOException("跨盘复制时源文件已变化，原文件已保留。");
            output.Flush(true);
        }
        finally {ArrayPool<byte>.Shared.Return(buffer);}
    }
}
