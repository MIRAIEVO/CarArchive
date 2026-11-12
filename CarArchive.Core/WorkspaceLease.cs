using System.Security.Cryptography;
using System.Text;

namespace CarArchive.Core;

public sealed class WorkspaceLease : IDisposable
{
    private static readonly HashSet<string> Active=new(StringComparer.Ordinal);
    private readonly List<(string Name,Mutex Mutex)> held=[];
    private WorkspaceLease() { }
    public static WorkspaceLease Acquire(string directory)
    {
        var lease=AcquireLegacy(directory);
        try { lease.Add("Local\\CarArchive-Workspace-"+PortableData.EnsureWorkspaceId(directory));return lease; }
        catch { lease.Dispose();throw; }
    }
    internal static WorkspaceLease AcquireLegacy(string directory)
    {
        var lease=new WorkspaceLease();var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)).ToUpperInvariant())));
        lease.Add("Local\\CarArchive-"+hash);return lease;
    }
    internal static WorkspaceLease AcquireIdentity(string id) { var lease=new WorkspaceLease();lease.Add("Local\\CarArchive-Workspace-"+id);return lease; }
    private void Add(string name)
    {
        lock(Active)
        {
            if(Active.Contains(name)) throw new IOException("该工作区正在运行，请先关闭其他版本后重试。");
            var mutex=new Mutex(false,name);bool acquired;
            try { acquired=mutex.WaitOne(0); }catch(AbandonedMutexException) { acquired=true; }
            if(!acquired) { mutex.Dispose();throw new IOException("该工作区正在运行，请先关闭其他版本后重试。"); }
            Active.Add(name);held.Add((name,mutex));
        }
    }
    public void Dispose()
    {
        lock(Active) foreach(var item in held.AsEnumerable().Reverse()) { item.Mutex.ReleaseMutex();item.Mutex.Dispose();Active.Remove(item.Name); }
        held.Clear();
    }
}
