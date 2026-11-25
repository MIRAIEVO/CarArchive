using DuplicateChecker.Core;
using System.Diagnostics;

var fixture=Directory.CreateTempSubdirectory("CarArchive-check-").FullName;
try
{
    string Folder(string name)=>Directory.CreateDirectory(Path.Combine(fixture,name)).FullName;
    string Write(string folder,string name,byte[] data)
    {
        var path=Path.Combine(folder,name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path,data);
        return path;
    }
    static void Check(bool condition,string test){if(!condition)throw new Exception(test);Console.WriteLine("PASS "+test);}
    static Details Classify(Match item)=>new(item.Status,item.Against);
    if(args.Contains("--move-only"))
    {
        const int count=1500;
        var inputs=Folder("move-input");
        var references=Folder("move-reference");
        var bytes=new byte[64*1024];
        var sampleRandom=new Random(19);
        for(var i=0;i<count;i++)
        {
            sampleRandom.NextBytes(bytes);
            Write(inputs,$"source-{i:D4}.jpg",bytes);
            if(i%3==0)Write(references,$"reference-{i:D4}.jpg",bytes);
        }
        var scanWatch=Stopwatch.StartNew();
        var scan=await Scanner.RunAsync(inputs,references,inputs,true,null,default);
        var scanning=scanWatch.Elapsed;
        var resultFolder=MoveRun.Create(scan);
        var moveWatch=Stopwatch.StartNew();
        var summary=await MoveRun.ExecuteAsync(resultFolder,false,null,default);
        var elapsed=moveWatch.Elapsed;
        Check(summary is {Done:count,Errors:0} &&
            Directory.GetFiles(Path.Combine(resultFolder,"已有")).Length==count/3 &&
            Directory.GetFiles(Path.Combine(resultFolder,"暂无")).Length==count-count/3,"automatic workflow classification counts");
        Console.WriteLine($"Synthetic {count:N0} x 64 KiB, {count/3} references: inspect {scanning.TotalSeconds:F3}s, move {elapsed.TotalSeconds:F3}s ({count/elapsed.TotalSeconds:N0} files/s).");
        return;
    }
    var target=Folder("target");
    var comparison=Folder("comparison");
    const int size=64*1024;
    var identical=new byte[size];
    new Random(12).NextBytes(identical);
    var different=(byte[])identical.Clone();different[9000]^=1;
    var duplicate=Write(target,"different-name.heic",identical);
    var unique=Write(target,Path.Combine("one","same-name.mov"),different);
    var reference=Write(comparison,Path.Combine("two","same-name.mov"),identical);
    var locked=Write(target,"locked.jpg",[3,5,8]);

    Task<ScanResult> Scan(bool recursive,CancellationToken ct=default)=>Scanner.RunAsync(target,comparison,target,recursive,null,ct);
    ScanResult preview;
    using(var handle=new FileStream(locked,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
    {
        preview=await Scan(true);
        var byPath=preview.Files.ToDictionary(row=>row.File.Path,Classify);
        Check(byPath[duplicate]==new Details(Classification.Existing,reference),"renamed identical file, recursive comparison");
        Check(byPath[unique].Status==Classification.Missing,"same length/name and same sampled blocks, different full contents");
        Check(byPath[locked].Status==Classification.Unknown,"locked file remains unverified");
        Check((await Scan(false)).Files.Length==2,"target recursion switch");
    }

    var run=MoveRun.Create(preview);
    using(var canceled=new CancellationTokenSource())
    {
        canceled.Cancel();
        try{await MoveRun.ExecuteAsync(run,false,null,canceled.Token);throw new Exception("expected cancellation");}
        catch(OperationCanceledException){}
        Check(File.Exists(duplicate)&&File.Exists(unique),"pre-cancel leaves originals in place");
    }
    var moved=await MoveRun.ExecuteAsync(run,false,null,default);
    Check(moved is {Done:2,Errors:0} && !File.Exists(duplicate) && !File.Exists(unique) && File.Exists(locked)
        && File.ReadAllBytes(reference).SequenceEqual(identical),"move leaves comparison and unverified file unchanged");
    Check(File.ReadAllBytes(Path.Combine(run,"暂无","one","same-name.mov")).SequenceEqual(different),"preserves relative paths and bytes");
    Check((await Scan(true)).Files.Length==1,"prior result ignored on repeated scan");
    var journal=Path.Combine(run,".operations","journal.jsonl");
    File.AppendAllText(journal,"{\"Index\":0");
    Check(MoveRun.ReadEntries(run).All(item=>item.State=="Moved"),"short journal tail is ignored after interruption");
    var entries=MoveRun.ReadEntries(run);
    var legacy=Path.Combine(run,".operations","0.json");
    File.WriteAllText(legacy,System.Text.Json.JsonSerializer.Serialize(new {Index=0,Item=entries[0] with {State="Moving"}}));
    File.WriteAllText(legacy+".new","unfinished");
    Check((await MoveRun.LoadAsync(run,default)).SequenceEqual(entries) && !File.Exists(legacy) && !File.Exists(legacy+".new") &&
        MoveRun.ReadEntries(run).SequenceEqual(entries),"old records compacted and temporary JSON removed without losing state");
    Write(target,"different-name.heic",[8]);
    var partial=await MoveRun.ExecuteAsync(run,true,null,default);
    Check(partial is {Done:1,Errors:1} && File.ReadAllBytes(duplicate).SequenceEqual(new byte[]{8}),"restore refuses to overwrite new file");
    File.Delete(duplicate);
    Check(await MoveRun.ExecuteAsync(run,true,null,default) is {Done:2,Errors:0} && File.ReadAllBytes(duplicate).SequenceEqual(identical),
        "continue restore using on-disk journal");
    preview=await Scan(true);
    File.WriteAllBytes(reference,[2,7,9]);
    var changed=await MoveRun.ExecuteAsync(MoveRun.Create(preview),false,null,default);
    Check(changed.Errors==1&&File.Exists(duplicate),"protects source when comparison evidence changes");
    var cross=Array.IndexOf(args,"--cross-volume");
    if(cross>=0)
    {
        var parent=Path.GetFullPath(args[cross+1]);
        Check(Path.GetPathRoot(parent)!=Path.GetPathRoot(fixture),"cross-volume test really uses another drive");
        var output=Directory.CreateDirectory(Path.Combine(parent,"CarArchive-cross-"+Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var crossSource=Folder("cross-target");
            var sourceFile=Write(crossSource,"same-bytes.mov",identical);
            var original=MediaFile.Read(sourceFile);
            var scan=await Scanner.RunAsync(crossSource,comparison,output,true,null,default);
            var result=MoveRun.Create(scan);
            Check(await MoveRun.ExecuteAsync(result,false,null,default) is {Done:1,Errors:0} &&
                File.ReadAllBytes(Path.Combine(result,"暂无","same-bytes.mov")).SequenceEqual(identical) && !File.Exists(sourceFile),"cross-volume copy verified and published");
            Check(await MoveRun.ExecuteAsync(result,true,null,default) is {Done:1,Errors:0} && MediaFile.Read(sourceFile)==original &&
                File.ReadAllBytes(sourceFile).SequenceEqual(identical) && !File.Exists(Path.Combine(result,".operation.lock")) &&
                !Directory.EnumerateFiles(result,"*.partial",SearchOption.AllDirectories).Any(),"cross-volume restore preserves original metadata and removes temporary files");
        }
        finally
        {
            if(Path.GetDirectoryName(output)==parent && Path.GetFileName(output).StartsWith("CarArchive-cross-",StringComparison.Ordinal))
                Directory.Delete(output,true);
        }
    }

    var largeTarget=Folder("benchmark-input");
    var largeComparison=Folder("benchmark-reference");
    var payload=new byte[256*1024];
    var random=new Random(733);
    for(var i=0;i<600;i++)
    {
        random.NextBytes(payload);
        Write(largeTarget,i+".jpg",payload);
        if(i%3==0)Write(largeComparison,i+".heic",payload);
    }
    var timer=Stopwatch.StartNew();
    var benchmark=await Scanner.RunAsync(largeTarget,largeComparison,largeTarget,true,null,default);
    Check(benchmark.Files.Count(item=>item.Status==Classification.Existing)==200,"benchmark fixture accuracy");
    Console.WriteLine($"Synthetic 600 x 256 KiB, 200 references: {600/timer.Elapsed.TotalSeconds:N0} targets/s ({timer.Elapsed.TotalSeconds:F3}s), enumeration {benchmark.EnumerationTime.TotalSeconds:F3}s");
    if(args.Contains("--large"))
    {
        const int count=10000;
        var collisionTargets=Folder("collision-targets");
        var collisionReferences=Folder("collision-references");
        var content=new byte[16*1024];
        for(var i=0;i<count;i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(4600,4),i);
            Write(collisionReferences,$"reference-{i:D5}.jpg",content);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(4600,4),i*2);
            Write(collisionTargets,$"target-{i:D5}.jpg",content);
        }
        timer.Restart();
        var collision=await Scanner.RunAsync(collisionTargets,collisionReferences,collisionTargets,true,null,default);
        Check(collision.Files.Count(item=>item.Status==Classification.Existing)==count/2 &&
            collision.Files.Count(item=>item.Status==Classification.Missing)==count/2,"10,000 × 10,000 same-size and same-sample files");
        Console.WriteLine($"Synthetic {count:N0} × {count:N0}, colliding samples at 16 KiB: {timer.Elapsed.TotalSeconds:F3}s ({count/timer.Elapsed.TotalSeconds:N0} targets/s).");
    }
}
finally
{
    if(Path.GetDirectoryName(fixture)==Path.TrimEndingDirectorySeparator(Path.GetTempPath()) &&
       Path.GetFileName(fixture).StartsWith("CarArchive-check-",StringComparison.Ordinal))
        Directory.Delete(fixture,true);
}

internal sealed record Details(Classification Status,string? Against);
