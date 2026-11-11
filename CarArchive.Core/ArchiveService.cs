using System.Security.Cryptography;

namespace CarArchive.Core;

public sealed class ArchiveService(IStateStore store)
{
    public Action<string>? DiagnosticTrace { get;set; }
    private readonly SemaphoreSlim mutex = new(1, 1);
    public static async Task<string> HashAsync(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }
    public async Task<ArchiveJob> PlanAsync(PhotoGroup group, string root, string brand, string folder, CancellationToken ct, WorkspaceState? state=null)
    {
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("请先选择归档根目录。");
        var directory = Path.Combine(Path.GetFullPath(root), NamingService.SafeSegment(brand), NamingService.SafeSegment(folder));
        var job = new ArchiveJob { GroupId = group.Id };
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var photo in group.Photos.Where(p => !p.Excluded).GroupBy(p=>p.AssetId.Length>0?p.AssetId:p.Id).Select(g=>g.First()))
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(photo.Path)) throw new FileNotFoundException("原图不存在：" + photo.Name, photo.Path);
            var source = Path.GetFullPath(photo.Path); var destination = Path.Combine(directory, Path.GetFileName(source));
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) throw new IOException("照片已经在该目录中，请检查目标目录。");
            var hash = await HashAsync(source, ct); var index = 2;
            while (File.Exists(destination) || reserved.Contains(destination))
            {
                if (File.Exists(destination) && await HashAsync(destination, ct) == hash)
                    throw new IOException($"发现相同内容的照片：{destination}。未移动本组，请排除此照片或选择其他目录。");
                destination = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(source)} ({index++}){Path.GetExtension(source)}");
            }
            reserved.Add(destination);
            var retain=photo.AssetId.Length>0 && state?.Groups.Any(g=>g.Id!=group.Id && !g.Archived && g.Photos.Any(p=>p.AssetId==photo.AssetId && !p.Excluded))==true;
            job.Entries.Add(new() { PhotoId = photo.Id,AssetId=photo.AssetId,RetainSource=retain, Source = source, Destination = destination, Temp = destination + "." + job.Id + ".partial", Hash = hash });
        }
        if (job.Entries.Count == 0) throw new InvalidOperationException("本组没有可归档的照片。");
        store.SaveJob(job); return job;
    }
    public async Task<ArchiveJob> ExecuteAsync(ArchiveJob job, CancellationToken ct)
    {
        await mutex.WaitAsync(ct);
        try
        {
            if (job.Status == "Completed" || job.Status == "Undone") return job;
            job.Status = "Pending"; store.SaveJob(job);
            foreach (var entry in job.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.Stage == "Done") continue;
                entry.Error = "";
                try { await MoveOneAsync(job, entry, ct); }
                catch (Exception e) { entry.Error = e.Message; job.Status = "Interrupted"; store.SaveJob(job); throw; }
            }
            job.Status = "Completed"; store.SaveJob(job);
            if (job.ParentJobId.Length > 0)
            {
                var original = store.Jobs().First(j => j.Id == job.ParentJobId); original.Status = "Undone"; store.SaveJob(original);
            }
            return job;
        }
        finally { mutex.Release(); }
    }
    private async Task MoveOneAsync(ArchiveJob job, MoveEntry entry, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(entry.Destination)!);
        if(entry.RestoreExisting && entry.Stage=="Planned")
        {
            if(!File.Exists(entry.Destination) || await HashAsync(entry.Destination,ct)!=entry.Hash || !File.Exists(entry.Source) || await HashAsync(entry.Source,ct)!=entry.Hash)
                throw new IOException("共享原图或归档副本已变化，撤销停止，文件均保留。");
            entry.Stage="Published"; store.SaveJob(job);
        }
        if (entry.Stage is "Planned" or "Copying")
        {
            if (File.Exists(entry.Destination)) throw new IOException("目标文件已出现，未覆盖：" + entry.Destination);
            if (!File.Exists(entry.Source)) throw new FileNotFoundException("源文件不存在，不能继续。", entry.Source);
            if (await HashAsync(entry.Source, ct) != entry.Hash) throw new IOException("源文件已被修改，已停止移动。");
            if (File.Exists(entry.Temp))
            {
                // Only remove a partial file owned by this specific journal entry.
                if (!entry.Temp.EndsWith("." + job.Id + ".partial", StringComparison.Ordinal)) throw new IOException("临时文件记录异常。");
                File.Delete(entry.Temp);
            }
            entry.Stage = "Copying"; store.SaveJob(job);
            await using (var source = new FileStream(entry.Source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true))
            await using (var target = new FileStream(entry.Temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await source.CopyToAsync(target, ct); await target.FlushAsync(ct); target.Flush(true); }
            if (await HashAsync(entry.Temp, ct) != entry.Hash) throw new IOException("复制校验失败，源文件已保留。");
            entry.Stage = "Verified"; store.SaveJob(job);
        }
        if (entry.Stage == "Verified")
        {
            if (File.Exists(entry.Temp))
            {
                if (await HashAsync(entry.Temp, ct) != entry.Hash) throw new IOException("临时文件校验失败。");
                File.Move(entry.Temp, entry.Destination, false);
            }
            else if (!File.Exists(entry.Destination) || await HashAsync(entry.Destination, ct) != entry.Hash)
                throw new IOException("中断后的目标文件无法核实，请检查操作记录。");
            entry.Stage = "Published"; store.SaveJob(job);
        }
        if (entry.Stage == "Published")
        {
            if (!File.Exists(entry.Destination) || await HashAsync(entry.Destination, ct) != entry.Hash) throw new IOException("目标照片缺失或被修改，源文件不会被删除。");
            if (!entry.RetainSource && File.Exists(entry.Source))
            {
                if (await HashAsync(entry.Source, ct) != entry.Hash) throw new IOException("源照片在移动过程中被修改，已保留两个文件。");
                DiagnosticTrace?.Invoke("Deleting "+entry.Source);
                File.Delete(entry.Source);
                DiagnosticTrace?.Invoke("Deleted "+entry.Source);
            }
            entry.Stage = "Done"; store.SaveJob(job);DiagnosticTrace?.Invoke("Journal Done "+entry.Destination);
        }
    }
    public async Task<ArchiveJob> UndoAsync(ArchiveJob original, CancellationToken ct)
    {
        var pending = store.Jobs().FirstOrDefault(j => j.ParentJobId == original.Id && j.Status is "Pending" or "Interrupted");
        if (pending is not null) return await ExecuteAsync(pending, ct);
        if (original.Direction != "Archive" || original.Status == "Undone") throw new InvalidOperationException("此记录不能撤销。");
        var reverse = new ArchiveJob { GroupId = original.GroupId, Direction = "Undo", ParentJobId = original.Id };
        foreach (var e in original.Entries.Where(e => e.Stage == "Done"))
        {
            var restoreExisting=File.Exists(e.Source);
            if (restoreExisting && (e.AssetId.Length==0 || await HashAsync(e.Source,ct)!=e.Hash)) throw new IOException("原位置已存在不同文件，撤销不会覆盖：" + e.Source);
            if (!File.Exists(e.Destination) || await HashAsync(e.Destination, ct) != e.Hash) throw new IOException("归档照片缺失或被修改，无法安全撤销：" + e.Destination);
            reverse.Entries.Add(new() { PhotoId = e.PhotoId,AssetId=e.AssetId,RestoreExisting=restoreExisting, Source = e.Destination, Destination = e.Source, Hash = e.Hash, Temp = e.Source + "." + reverse.Id + ".partial" });
        }
        if (reverse.Entries.Count == 0) throw new InvalidOperationException("没有已经完成的移动可撤销。可先恢复中断任务。");
        store.SaveJob(reverse); return await ExecuteAsync(reverse, ct);
    }
    public static void Reconcile(WorkspaceState state, IEnumerable<ArchiveJob> jobs)
    {
        foreach (var job in jobs.OrderBy(j => j.CreatedAt))
        {
            var group = state.Groups.FirstOrDefault(g => g.Id == job.GroupId); if (group is null) continue;
            foreach (var e in job.Entries.Where(e => e.Stage == "Done"))
            { foreach(var photo in group.Photos.Where(p=>p.Id==e.PhotoId || e.AssetId.Length>0 && p.AssetId==e.AssetId)) photo.Path=e.Destination; }
            if (job.Status == "Completed") { group.Archived = job.Direction == "Archive"; group.State = job.Direction == "Archive" ? "已归档" : "已撤销 · 待复核"; }
            else if (job.Status is "Pending" or "Interrupted") { group.State = "归档中断 · 请恢复"; }
            group.Refresh();
        }
    }
}
