namespace CarArchive.Core;

public sealed class GroupTaskCoordinator
{
    private readonly SemaphoreSlim recognition=new(2,2);
    private readonly SemaphoreSlim archive=new(1,1);
    private readonly Dictionary<string,CancellationTokenSource> active=[];
    private readonly object gate=new();
    public async Task RunAsync(PhotoGroup group,string kind,bool fileOperation,Func<CancellationToken,Task> action,Action changed,CancellationToken cancellationToken=default)
    {
        CancellationTokenSource cts;
        lock(gate)
        {
            if(active.ContainsKey(group.Id)) throw new InvalidOperationException("本组已有任务，请等待完成或取消该任务。");
            cts=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); active[group.Id]=cts;
        }
        var semaphore=fileOperation?archive:recognition;
        var acquired=false;
        group.TaskKind=kind; group.LastError=""; group.TaskState=GroupTaskState.Queued; changed();
        try
        {
            await semaphore.WaitAsync(cts.Token); acquired=true;
            group.TaskState=GroupTaskState.Running; changed();
            await action(cts.Token);
            group.TaskState=GroupTaskState.Completed;
        }
        catch(OperationCanceledException) when(cts.IsCancellationRequested) { group.TaskState=GroupTaskState.Canceled; group.LastError="已取消"; }
        catch(Exception e) { group.LastError=e.Message; group.TaskState=GroupTaskState.Failed; }
        finally
        {
            if(acquired) semaphore.Release();
            lock(gate) active.Remove(group.Id);
            cts.Dispose(); changed();
        }
    }
    public void Cancel(string groupId) { lock(gate) if(active.TryGetValue(groupId,out var cts)) cts.Cancel(); }
    public void CancelAll() { lock(gate) foreach(var cts in active.Values) cts.Cancel(); }
    public async Task<T> WithRecognitionSlotAsync<T>(Func<CancellationToken,Task<T>> action,CancellationToken ct)
    {
        await recognition.WaitAsync(ct);try { return await action(ct); }finally { recognition.Release(); }
    }
}
