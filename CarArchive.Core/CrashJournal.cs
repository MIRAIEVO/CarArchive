using System.Text.Json;
using System.Text.RegularExpressions;

namespace CarArchive.Core;

/// <summary>Bounded recent actions. Clean sessions leave no action log; incomplete sessions become crash reports.</summary>
public sealed class CrashJournal : IDisposable
{
    public static CrashJournal? Current { get; set; }
    public static void Trace(string action,string? group=null,int? count=null)=>Current?.Record(action,group,count);
    private sealed record Breadcrumb(DateTimeOffset At,int Thread,string Action,string? Group,int? Count);
    private readonly object gate=new();
    private readonly Queue<Breadcrumb> actions=new();
    private readonly string directory,pending;
    private readonly Timer timer;
    private readonly int capacity;
    private bool closed,crashed;
    public CrashJournal(string directory,int capacity=256)
    {
        this.directory=directory;this.capacity=Math.Clamp(capacity,8,1024);pending=Path.Combine(directory,"session-pending.json");
        try
        {
            Directory.CreateDirectory(directory);
            if(File.Exists(pending))
            {
                var previous=File.ReadAllText(pending);
                SaveReport("UnexpectedExit",null,previous);
                File.Delete(pending);
            }
        } catch { /* Diagnostics must never prevent startup. */ }
        Record("Application.Start");Flush();
        timer=new(_=>Flush(),null,1000,1000);
    }
    public void Record(string action,string? group=null,int? count=null)
    {
        lock(gate)
        {
            if(closed) return;
            if(actions.Count==capacity) actions.Dequeue();
            actions.Enqueue(new(DateTimeOffset.Now,Environment.CurrentManagedThreadId,action[..Math.Min(action.Length,160)],group,count));
        }
    }
    public void Flush()
    {
        lock(gate)
        {
            if(closed || crashed) return;
            try { NamingGuideStore.WriteAtomic(pending,Snapshot(),false); } catch { }
        }
    }
    public void Capture(string origin,Exception error)
    {
        lock(gate)
        {
            if(closed || crashed) return;
            crashed=true;Record("Crash."+origin);
            try
            {
                SaveReport(origin,error,Snapshot());
                if(File.Exists(pending)) File.Delete(pending);
            } catch { }
        }
    }
    private string Snapshot()=>JsonSerializer.Serialize(new { Version=1,StartedProcess=Environment.ProcessId,AppVersion=System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),RecentActions=actions.ToArray() },StateStore.Json);
    private void SaveReport(string reason,Exception? error,string recent)
    {
        var exception=error is null?null:Regex.Replace(error.ToString(),@"AIza[\w-]{20,}|(?i:api[_ -]?key\s*[=:]\s*)[^\s,;]+","[redacted]");
        if(exception?.Length>24000) exception=exception[..24000];
        var path=Path.Combine(directory,"crash-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")[..6]+".json");
        NamingGuideStore.WriteAtomic(path,JsonSerializer.Serialize(new { At=DateTimeOffset.Now,Reason=reason,Exception=exception,Session=JsonSerializer.Deserialize<JsonElement>(recent) },new JsonSerializerOptions(StateStore.Json) { WriteIndented=true }),false);
        foreach(var old in Directory.EnumerateFiles(directory,"crash-*.json").OrderByDescending(File.GetLastWriteTimeUtc).Skip(10)) File.Delete(old);
    }
    public void Complete()
    {
        lock(gate)
        {
            if(closed) return;closed=true;timer.Dispose();
            try { if(File.Exists(pending)) File.Delete(pending); } catch { }
        }
    }
    public void Dispose()
    {
        // A caller must explicitly mark a clean exit. Disposing an unfinished session keeps its checkpoint.
        lock(gate) { if(!closed && !crashed) Flush();closed=true;timer.Dispose(); }
    }
}
