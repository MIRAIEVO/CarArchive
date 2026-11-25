using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Security.Cryptography;

namespace DuplicateChecker.Core;

public enum Classification { Existing, Missing, Unknown }
public record MediaFile(string Path,long Size,long Modified,long Created)
{
    public static MediaFile Read(string path)
    {
        var file=new FileInfo(path);
        if(file.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("不处理链接或重解析点。");
        return new(path,file.Length,file.LastWriteTimeUtc.Ticks,file.CreationTimeUtc.Ticks);
    }
    public void Verify(){if(this!=Read(Path))throw new IOException("文件已变化："+Path);}
}
public record Match(MediaFile File,Classification Status,string? Against=null,string? Hash=null,string? Issue=null);
public record ScanResult(string Target,string Comparison,string Output,Match[] Files,string[] Warnings,TimeSpan EnumerationTime,TimeSpan MatchTime);
public record ScanProgress(string Stage,int Done,int Total);

public static class Paths
{
    public const string Marker=".duplicate-checker.json";
    public static string Canonical(string path)=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    public static bool Within(string root,string path)
    {
        var relative=Path.GetRelativePath(root,path);
        return !Path.IsPathRooted(relative) && relative!=".." && !relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal);
    }
    public static void NoLinks(string path)
    {
        for(var item=path;item is not null;item=Path.GetDirectoryName(item))
        {
            FileAttributes attributes;
            try {attributes=File.GetAttributes(item);}
            catch(FileNotFoundException){continue;}
            catch(DirectoryNotFoundException){continue;}
            if(attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("不使用链接目录或文件："+item);
        }
    }
    public static void Validate(string target,string comparison,string output)
    {
        if(!Directory.Exists(target)||!Directory.Exists(comparison)||!Directory.Exists(output))throw new IOException("请选择三个有效目录。");
        if(Within(target,comparison)||Within(comparison,target)||Within(comparison,output))throw new IOException("目标和比较目录不能重叠，输出不能位于比较目录内。");
        NoLinks(target);NoLinks(comparison);NoLinks(output);
    }
}

public static class Scanner
{
    private record Fingerprint(MediaFile File,string? Key);
    private sealed class SampleBucket
    {
        private readonly MediaFile[] files;
        private readonly Lazy<Task<(string? Hash,MediaFile File)>> first;
        private readonly Lazy<Task<(Dictionary<string,string> Paths,bool Uncertain)>> remaining;

        public SampleBucket(IEnumerable<MediaFile> candidates,ConcurrentQueue<string> warnings,CancellationToken ct)
        {
            files=candidates.OrderBy(file=>file.Path,StringComparer.OrdinalIgnoreCase).ToArray();
            first=new(()=>Hash(files[0]));
            remaining=new(async ()=>
            {
                var paths=new Dictionary<string,string>(StringComparer.Ordinal);
                var uncertain=false;
                foreach(var file in files.Skip(1))
                {
                    var candidate=await Hash(file);
                    if(candidate.Hash is { } hash) paths.TryAdd(hash,candidate.File.Path);
                    else uncertain=true;
                }
                return (paths,uncertain);
            });

            async Task<(string? Hash,MediaFile File)> Hash(MediaFile file)
            {
                try{return (await FullHashAsync(file,ct),file);}
                catch(Exception error) when(IsIo(error)){warnings.Enqueue(file.Path+"："+error.Message);return (null,file);}
            }
        }

        public async Task<(string? Path,bool Uncertain)> FindAsync(string hash)
        {
            var candidate=await first.Value;
            if(candidate.Hash==hash)return(candidate.File.Path,false);
            if(files.Length==1)return(null,candidate.Hash is null);
            var (paths,uncertain)=await remaining.Value;
            return(paths.GetValueOrDefault(hash),uncertain||candidate.Hash is null);
        }
    }
    public static Task<ScanResult> RunAsync(string target,string comparison,string output,bool recursive,IProgress<ScanProgress>? progress,CancellationToken ct)=>
        Task.Run(async()=>{
            target=Paths.Canonical(target);comparison=Paths.Canonical(comparison);output=Paths.Canonical(output);
            Paths.Validate(target,comparison,output);
            var timer=Stopwatch.StartNew();
            var warnings=new ConcurrentQueue<string>();
            progress?.Report(new("枚举目录",0,0));
            var referenceScan=Task.Run(()=>
            {
                var files=Enumerate(comparison,true,false,warnings,ct,out var partial);
                return (files,partial);
            },ct);
            var targetFiles=Enumerate(target,recursive,true,warnings,ct,out _);
            var (compareFiles,incomplete)=await referenceScan;
            var enumeration=timer.Elapsed;timer.Restart();
            var sizes=targetFiles.Where(file=>file.Issue is null).Select(file=>file.File.Size).ToHashSet();
            var candidates=compareFiles.Where(file=>file.Issue is null&&sizes.Contains(file.File.Size)).Select(file=>file.File).ToArray();
            var samples=new ConcurrentDictionary<long,ConcurrentBag<Fingerprint>>();
            var concurrency=Math.Clamp(Environment.ProcessorCount/2,2,8);
            var options=new ParallelOptions{MaxDegreeOfParallelism=concurrency,CancellationToken=ct};
            var count=0;
            progress?.Report(new("筛选比较文件",0,candidates.Length));
            await Parallel.ForEachAsync(candidates,options,(file,token)=>{
                string? key=null;
                try {key=Sample(file,token);}
                catch(Exception error) when(IsIo(error)){warnings.Enqueue(file.Path+"："+error.Message);}
                samples.GetOrAdd(file.Size,_=>new()).Add(new(file,key));
                var done=Interlocked.Increment(ref count);
                if(done%128==0||done==candidates.Length)progress?.Report(new("筛选比较文件",done,candidates.Length));
                return ValueTask.CompletedTask;
            });
            var index=samples.ToDictionary(item=>item.Key,item=>(
                BySample:item.Value.Where(file=>file.Key is not null).GroupBy(file=>file.Key!,StringComparer.Ordinal)
                    .ToDictionary(group=>group.Key,group=>new SampleBucket(group.Select(file=>file.File),warnings,ct),StringComparer.Ordinal),
                Uncertain:item.Value.Any(file=>file.Key is null)));
            count=0;var results=new Match[targetFiles.Count];
            progress?.Report(new("确认文件内容",0,results.Length));
            await Parallel.ForEachAsync(Enumerable.Range(0,targetFiles.Count),options,async (position,token)=>{
                var item=targetFiles[position];var file=item.File;
                try
                {
                    if(item.Issue is not null){results[position]=item;return;}
                    string? hash=null;string? match=null;
                    var uncertain=incomplete;
                    if(index.TryGetValue(file.Size,out var bucket))
                    {
                        var key=Sample(file,token);
                        uncertain|=bucket.Uncertain;
                        if(bucket.BySample.TryGetValue(key,out var matching))
                        {
                            hash=await FullHashAsync(file,token);
                            var result=await matching.FindAsync(hash);
                            match=result.Path;
                            uncertain|=result.Uncertain;
                        }
                    }
                    else
                    {
                        using var read=File.OpenHandle(file.Path,FileMode.Open,FileAccess.Read,FileShare.Read);
                        file.Verify();
                    }
                    results[position]=match is not null?new(file,Classification.Existing,match,hash):
                        uncertain?new(file,Classification.Unknown,Issue:"部分比较范围无法核实"):
                        new(file,Classification.Missing,Hash:hash);
                }
                catch(Exception error) when(IsIo(error)){results[position]=new(file,Classification.Unknown,Issue:error.Message);}
                finally
                {
                    var done=Interlocked.Increment(ref count);
                    if(done%64==0 || done==results.Length)progress?.Report(new("确认文件内容",done,results.Length));
                }
            });
            return new ScanResult(target,comparison,output,results,warnings.ToArray(),enumeration,timer.Elapsed);
        },ct);

    private static List<Match> Enumerate(string root,bool recursive,bool skipResults,ConcurrentQueue<string> warnings,CancellationToken ct,out bool incomplete)
    {
        incomplete=false;var rows=new List<Match>();var queue=new Stack<string>();queue.Push(root);
        while(queue.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if(skipResults && File.Exists(Path.Combine(directory,Paths.Marker)))continue;
                var entries=new FileSystemEnumerable<(string Path,FileAttributes Attributes,long Size,long Modified,long Created)>(
                    directory,(ref FileSystemEntry entry)=>(entry.ToFullPath(),entry.Attributes,entry.Length,
                        entry.LastWriteTimeUtc.Ticks,entry.CreationTimeUtc.Ticks),new EnumerationOptions{AttributesToSkip=0,IgnoreInaccessible=false});
                foreach(var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if(entry.Attributes.HasFlag(FileAttributes.ReparsePoint))continue;
                    if(entry.Attributes.HasFlag(FileAttributes.Directory)){if(recursive)queue.Push(entry.Path);continue;}
                    rows.Add(new(new(entry.Path,entry.Size,entry.Modified,entry.Created),Classification.Unknown));
                }
            }
            catch(Exception error) when(IsIo(error)){incomplete=true;warnings.Enqueue(directory+"："+error.Message);}
        }
        return rows;
    }
    internal static bool IsIo(Exception error)=>error is IOException or UnauthorizedAccessException;
    private static FileStream Open(MediaFile file)
    {
        file.Verify();
        return new FileStream(file.Path,FileMode.Open,FileAccess.Read,FileShare.Read,128*1024,FileOptions.SequentialScan|FileOptions.Asynchronous);
    }
    private static string Sample(MediaFile file,CancellationToken ct)
    {
        using var read=File.OpenHandle(file.Path,FileMode.Open,FileAccess.Read,FileShare.Read,FileOptions.RandomAccess);
        Span<byte> bytes=stackalloc byte[4096*3];var used=0;
        if(file.Size<=bytes.Length){ct.ThrowIfCancellationRequested();ReadAt(bytes[..(int)file.Size],0);used=(int)file.Size;}
        else
        {
            ReadAt(bytes[..4096],0);
            ReadAt(bytes.Slice(4096,4096),(file.Size-4096)/2);
            ReadAt(bytes.Slice(8192,4096),file.Size-4096);
            used=bytes.Length;
        }
        file.Verify();return Convert.ToHexString(SHA256.HashData(bytes[..used]));

        void ReadAt(Span<byte> destination,long offset)
        {
            while(!destination.IsEmpty)
            {
                ct.ThrowIfCancellationRequested();
                var length=RandomAccess.Read(read,destination,offset);
                if(length==0)throw new EndOfStreamException("文件在抽样时缩短。");
                destination=destination[length..];
                offset+=length;
            }
        }
    }
    internal static async Task<string> FullHashAsync(MediaFile file,CancellationToken ct)
    {
        await using var read=Open(file);var hash=Convert.ToHexString(await SHA256.HashDataAsync(read,ct));file.Verify();return hash;
    }
}
