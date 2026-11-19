namespace CarArchive.Core;

/// <summary>Byte-preserving independent copies and journaled recycle operations. Never deletes source photos itself.</summary>
public sealed class PhotoLifecycleService(IPhotoOperationStore store,string managedDirectory,IPhotoRecycler recycler)
{
    public string ManagedDirectory { get; } = Path.GetFullPath(managedDirectory).TrimEnd(Path.DirectorySeparatorChar);
    public static bool Unfinished(PhotoFileOperation operation)=>operation.Status is "Pending" or "Interrupted" or "FilesReady";
    public async Task<PhotoFileOperation> PlanCopyAsync(PhotoGroup target,string sourceGroupId,string kind,CancellationToken ct)
    {
        var operation=new PhotoFileOperation { GroupId=target.Id,SourceGroupId=sourceGroupId,Kind=kind };
        foreach(var set in target.Photos.GroupBy(p=>Path.GetFullPath(p.Path),StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();var source=set.First();var path=Path.GetFullPath(source.Path);
            var info=ValidateSource(path);var entry=new PhotoFileEntry { PhotoIds=set.Select(p=>p.Id).ToList(),SourceAssetId=source.AssetId,Source=path,Length=info.Length,ModifiedTicks=info.LastWriteTimeUtc.Ticks,CreatedTicks=info.CreationTimeUtc.Ticks };
            entry.Destination=Path.Combine(ManagedDirectory,entry.NewAssetId,Path.GetFileName(path));entry.Temporary=entry.Destination+"."+operation.Id+".partial";
            entry.Hash=await ArchiveService.HashAsync(path,ct);ValidateUnchanged(entry);operation.Entries.Add(entry);
        }
        if(operation.Entries.Count==0) throw new InvalidOperationException("没有可复制的照片。");
        store.SavePhotoOperation(operation);return operation;
    }
    public async Task CopyAsync(PhotoFileOperation operation,IProgress<string>? progress,CancellationToken ct)
    {
        if(operation.Kind is not ("Copy" or "Isolate")) throw new InvalidOperationException("该记录不是照片复制操作。");
        operation.Status="Pending";store.SavePhotoOperation(operation);
        try
        {
            var index=0;
            foreach(var entry in operation.Entries)
            {
                ct.ThrowIfCancellationRequested();progress?.Report($"复制并校验 {++index}/{operation.Entries.Count}");
                ValidateManagedPath(entry.Destination);ValidateManagedPath(entry.Temporary);
                if(entry.Stage=="Attached") continue;
                if(File.Exists(entry.Destination))
                {
                    if(entry.Stage is "Planned" or "Copying" || await ArchiveService.HashAsync(entry.Destination,ct)!=entry.Hash) throw new IOException("副本目标已存在或被修改，未覆盖。");
                    entry.CopiedModifiedTicks=File.GetLastWriteTimeUtc(entry.Destination).Ticks;entry.Stage="Ready";store.SavePhotoOperation(operation);continue;
                }
                ValidateUnchanged(entry);
                if(await ArchiveService.HashAsync(entry.Source,ct)!=entry.Hash) throw new IOException("复制前原照片已发生变化，未使用旧记录。");
                Directory.CreateDirectory(Path.GetDirectoryName(entry.Destination)!);ValidateManagedPath(entry.Destination);
                if(File.Exists(entry.Temporary)) File.Delete(entry.Temporary);
                entry.Stage="Copying";entry.Error="";store.SavePhotoOperation(operation);
                await using(var input=new FileStream(entry.Source,FileMode.Open,FileAccess.Read,FileShare.Read,131072,FileOptions.Asynchronous|FileOptions.SequentialScan))
                await using(var output=new FileStream(entry.Temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,FileOptions.Asynchronous|FileOptions.WriteThrough))
                { await input.CopyToAsync(output,ct);await output.FlushAsync(ct);output.Flush(true); }
                ValidateUnchanged(entry);
                if(await ArchiveService.HashAsync(entry.Temporary,ct)!=entry.Hash) throw new IOException("独立副本内容校验失败，原照片未更改。");
                entry.Stage="Verified";store.SavePhotoOperation(operation);
                File.Move(entry.Temporary,entry.Destination,false);entry.CopiedModifiedTicks=File.GetLastWriteTimeUtc(entry.Destination).Ticks;entry.Stage="Ready";store.SavePhotoOperation(operation);
            }
            operation.Status="FilesReady";store.SavePhotoOperation(operation);
        }
        catch(Exception e)
        {
            operation.Status="Interrupted";var current=operation.Entries.FirstOrDefault(x=>x.Stage is not ("Ready" or "Attached"));if(current is not null) current.Error=e is OperationCanceledException?"已取消":e.Message;
            store.SavePhotoOperation(operation);throw;
        }
    }
    public static void AttachCopies(PhotoGroup group,WorkspaceState state,PhotoFileOperation operation)
    {
        // Validate the entire attachment set before mutating even the first model reference.
        var prepared=new List<(PhotoFileEntry Entry,List<Photo> Photos,ImageAsset Asset)>();
        var seen=new HashSet<string>();
        foreach(var entry in operation.Entries)
        {
            if(entry.Stage is not ("Ready" or "Attached")) throw new IOException("副本尚未完成校验，不能附加到分组。");
            var members=group.Photos.Where(p=>entry.PhotoIds.Contains(p.Id)).ToList();
            if(members.Count!=entry.PhotoIds.Distinct().Count() || members.Count==0 || members.Any(p=>!seen.Add(p.Id))) throw new IOException("复制记录与分组不一致，请检查操作记录。");
            var file=new FileInfo(entry.Destination);
            if(!file.Exists || file.Length!=entry.Length || file.Attributes.HasFlag(FileAttributes.ReparsePoint) || entry.CopiedModifiedTicks!=0 && file.LastWriteTimeUtc.Ticks!=entry.CopiedModifiedTicks)
                throw new IOException("独立副本不存在或已发生变化，未附加到分组。");
            var template=members[0];var asset=state.Assets.FirstOrDefault(a=>a.Id==entry.NewAssetId)
                ?? new ImageAsset { Id=entry.NewAssetId,OriginalPath=entry.Destination,Length=entry.Length,ModifiedTicks=file.LastWriteTimeUtc.Ticks,Hash=entry.Hash,PreviewPath=template.PreviewPath,ThumbnailPath=template.ThumbnailPath,CapturedAt=template.CapturedAt,Error=template.Error };
            prepared.Add((entry,members,asset));
        }
        foreach(var item in prepared)
        {
            if(!state.Assets.Contains(item.Asset)) state.Assets.Add(item.Asset);
            foreach(var photo in item.Photos) { photo.Path=item.Entry.Destination;photo.OriginalPath=item.Entry.Destination;photo.Attach(item.Asset); }
        }
    }
    public void CompleteCopy(PhotoFileOperation operation)
    {
        if(operation.Status!="FilesReady" || operation.Entries.Any(e=>e.Stage is not ("Ready" or "Attached"))) throw new IOException("照片副本尚未全部就绪。");
        foreach(var entry in operation.Entries) entry.Stage="Attached";operation.Status="Completed";store.SavePhotoOperation(operation);
    }
    public async Task AbandonCopyAsync(PhotoFileOperation operation,CancellationToken ct)
    {
        if(operation.Kind is not ("Copy" or "Isolate") || operation.Status=="Completed") throw new InvalidOperationException("该操作不能取消清理。");
        foreach(var entry in operation.Entries)
        {
            ct.ThrowIfCancellationRequested();ValidateManagedPath(entry.Temporary);ValidateManagedPath(entry.Destination);
            if(File.Exists(entry.Temporary)) File.Delete(entry.Temporary);
            if(File.Exists(entry.Destination))
            { if(await ArchiveService.HashAsync(entry.Destination,ct)!=entry.Hash) throw new IOException("副本已被修改，未自动清理。");File.Delete(entry.Destination); }
            var folder=Path.GetDirectoryName(entry.Destination)!;if(Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        operation.Status="Canceled";store.SavePhotoOperation(operation);
    }
    public async Task<PhotoFileOperation> PlanRecycleAsync(PhotoGroup group,IReadOnlyList<Photo> photos,WorkspaceState snapshot,CancellationToken ct,IReadOnlyDictionary<string,string>? expectedHashes=null)
    {
        if(group.Archived || group.FilesPending) throw new InvalidOperationException("仅可回收已完成独立副本准备的待处理组照片。");
        var chosen=photos.Select(p=>p.Id).ToHashSet();if(chosen.Count==0 || photos.Any(p=>!group.Photos.Contains(p))) throw new InvalidOperationException("请重新选择本组照片。");
        var operation=new PhotoFileOperation { Kind="Recycle",GroupId=group.Id };
        foreach(var set in photos.GroupBy(p=>Path.GetFullPath(p.Path),StringComparer.OrdinalIgnoreCase))
        {
            var path=set.Key;
            if(snapshot.Groups.Any(g=>g.Id!=group.Id && g.Photos.Any(p=>Path.GetFullPath(p.Path).Equals(path,StringComparison.OrdinalIgnoreCase)))) throw new IOException("此原图仍被其他组引用，请先完成独立副本迁移。");
            if(group.Photos.Any(p=>!chosen.Contains(p.Id) && Path.GetFullPath(p.Path).Equals(path,StringComparison.OrdinalIgnoreCase))) throw new IOException("本组还有未选中的照片引用，请一并选择后再回收。");
            var info=ValidateSource(path);var entry=new PhotoFileEntry { Source=path,SourceAssetId=set.First().AssetId,PhotoIds=set.Select(p=>p.Id).ToList(),Length=info.Length,ModifiedTicks=info.LastWriteTimeUtc.Ticks,CreatedTicks=info.CreationTimeUtc.Ticks };
            entry.Hash=await ArchiveService.HashAsync(path,ct);ValidateUnchanged(entry);
            if(expectedHashes is not null && (!expectedHashes.TryGetValue(path,out var expected) || expected!=entry.Hash))
                throw new IOException("待回收文件自确认后发生变化，未回收。");
            operation.Entries.Add(entry);
        }
        store.SavePhotoOperation(operation);return operation;
    }
    public async Task RecycleAsync(PhotoFileOperation operation,Func<PhotoFileEntry,Task> removed,IProgress<string>? progress,CancellationToken ct)
    {
        if(operation.Kind!="Recycle") throw new InvalidOperationException("该记录不是回收站操作。");
        operation.Status="Pending";store.SavePhotoOperation(operation);var failed=false;var index=0;
        try
        {
            foreach(var entry in operation.Entries)
            {
                ct.ThrowIfCancellationRequested();progress?.Report($"移入回收站 {++index}/{operation.Entries.Count}");
                if(entry.Stage=="Removed") { await removed(entry);continue; }
                if(entry.Stage=="Recycled") { await removed(entry);entry.Stage="Removed";store.SavePhotoOperation(operation);continue; }
                if(entry.Stage=="Recycling") { failed=true;entry.Error="上次回收结果不明，请核对回收站后处理，未再次删除同一路径。";store.SavePhotoOperation(operation);continue; }
                try
                {
                    ValidateUnchanged(entry);if(await ArchiveService.HashAsync(entry.Source,ct)!=entry.Hash) throw new IOException("原照片已变化，未回收替换后的文件。");
                    entry.Stage="Recycling";entry.Error="";store.SavePhotoOperation(operation);
                    entry.RecycleIdentity=await recycler.RecycleAsync(entry,ct);
                    entry.Stage="Recycled";store.SavePhotoOperation(operation);
                    await removed(entry);entry.Stage="Removed";store.SavePhotoOperation(operation);
                }
                catch(OperationCanceledException) { throw; }
                catch(Exception e) { failed=true;entry.Error=e.Message;if(e is PhotoRecycleRejectedException || entry.Stage is not ("Recycling" or "Recycled")) entry.Stage="Failed";store.SavePhotoOperation(operation); }
            }
            operation.Status=failed?"Interrupted":"Completed";store.SavePhotoOperation(operation);
        }
        catch { operation.Status="Interrupted";store.SavePhotoOperation(operation);throw; }
    }
    public void ValidateManagedPath(string path)
    {
        var full=Path.GetFullPath(path);if(!full.StartsWith(ManagedDirectory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("独立副本路径不在本版本数据目录内。");
        if(File.Exists(full) && File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("独立副本文件不能是符号链接。");
        for(var directory=Path.GetDirectoryName(full);directory is not null;directory=Path.GetDirectoryName(directory))
        { if(Directory.Exists(directory) && File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("独立副本目录不能是符号链接或联接。");if(directory.Equals(Path.GetPathRoot(full),StringComparison.OrdinalIgnoreCase)) break; }
    }
    private static FileInfo ValidateSource(string path)
    {
        var info=new FileInfo(path);if(!info.Exists) throw new FileNotFoundException("照片源文件不存在，请恢复或重新导入。",path);
        if(info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("不直接操作符号链接照片，请导入普通文件。");return info;
    }
    public static void ValidateUnchanged(PhotoFileEntry entry)
    {
        var info=ValidateSource(entry.Source);
        if(info.Length!=entry.Length || info.LastWriteTimeUtc.Ticks!=entry.ModifiedTicks || info.CreationTimeUtc.Ticks!=entry.CreatedTicks) throw new IOException("照片文件已发生变化，已停止操作。");
    }
}
