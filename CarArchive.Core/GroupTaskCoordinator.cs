namespace CarArchive.Core;

public sealed class GroupTaskCoordinator
{
    private readonly SemaphoreSlim recognition=new(2,2);
    private readonly SemaphoreSlim archive=new(1,1);
    private readonly Dictionary<string,CancellationTokenSource> active=[];
    private readonly object gate=new();
    public Task RunAsync(PhotoGroup group,string kind,bool fileOperation,Func<CancellationToken,Task> action,Action changed,CancellationToken cancellationToken=default)
        => RunManyAsync([group],kind,fileOperation,action,_=>changed(),cancellationToken);
    public async Task RunManyAsync(IReadOnlyList<PhotoGroup> groups,string kind,bool fileOperation,Func<CancellationToken,Task> action,Action<PhotoGroup> changed,CancellationToken cancellationToken=default)
    {
        if(groups.Count==0) throw new ArgumentException("任务至少需要一个分组。");
        var targets=groups.Distinct().ToList();
        CancellationTokenSource cts;
        lock(gate)
        {
            if(targets.Any(group=>active.ContainsKey(group.Id))) throw new InvalidOperationException("所选分组已有任务，请等待完成或取消该任务。");
            cts=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);foreach(var group in targets) active[group.Id]=cts;
        }
        var semaphore=fileOperation?archive:recognition;
        var acquired=false;
        foreach(var group in targets) { group.TaskKind=kind; group.LastError=""; group.TaskState=GroupTaskState.Queued; changed(group); }
        try
        {
            await semaphore.WaitAsync(cts.Token); acquired=true;
            foreach(var group in targets) { group.TaskState=GroupTaskState.Running; changed(group); }
            await action(cts.Token);
            foreach(var group in targets) group.TaskState=GroupTaskState.Completed;
        }
        catch(OperationCanceledException) when(cts.IsCancellationRequested) { foreach(var group in targets) { group.TaskState=GroupTaskState.Canceled; group.LastError="已取消"; } }
        catch(Exception e) { foreach(var group in targets) { group.LastError=e.Message; group.TaskState=GroupTaskState.Failed; } }
        finally
        {
            if(acquired) semaphore.Release();
            lock(gate) foreach(var group in targets) active.Remove(group.Id);
            cts.Dispose();foreach(var group in targets) changed(group);
        }
    }
    public void Cancel(string groupId) { lock(gate) if(active.TryGetValue(groupId,out var cts)) cts.Cancel(); }
    public void CancelAll() { lock(gate) foreach(var cts in active.Values) cts.Cancel(); }
    public async Task<T> WithRecognitionSlotAsync<T>(Func<CancellationToken,Task<T>> action,CancellationToken ct)
    {
        await recognition.WaitAsync(ct);try { return await action(ct); }finally { recognition.Release(); }
    }
}
