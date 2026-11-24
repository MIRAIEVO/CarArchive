using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace CarArchive.Core;

/// <summary>Bounded recent actions. Clean sessions leave no action log; incomplete sessions become crash reports.</summary>
public sealed class CrashJournal : IDisposable
{
    public static CrashJournal? Current { get; set; }
    public static void Trace(string action,string? group=null,int? count=null)=>Current?.Record(action,group,count);
    public static void Fault(string action,Exception error,string? group=null)=>
        Trace($"{action}:{error.GetType().Name}:0x{error.HResult:X8}",group);
    private sealed record Breadcrumb(DateTimeOffset At,int Thread,string Action,string? Group,int? Count);
    private readonly object gate=new();
    private readonly Queue<Breadcrumb> actions=new();
    private readonly string directory,pending;
    private readonly Timer timer;
    private readonly int capacity;
    private readonly DateTimeOffset startedAt=DateTimeOffset.UtcNow;
    private long uiHeartbeat;
    private long activityVersion;
    private long flushedVersion=-1;
    private bool closed,crashed;
    public CrashJournal(string directory,int capacity=256)
    {
        this.directory=directory;this.capacity=Math.Clamp(capacity,8,1024);pending=Path.Combine(directory,"session-pending.json");
        try
        {
            Directory.CreateDirectory(directory);PruneReports();
            if(File.Exists(pending))
            {
                var previous=File.ReadAllText(pending);
                using var document=JsonDocument.Parse(previous);
                var session=document.RootElement;
                var marker=session.TryGetProperty("StartedProcess",out var pid)&&pid.TryGetInt32(out var number)
                    ?Path.Combine(directory,$"exit-{number}.txt"):null;
                int? exitCode=marker is not null && File.Exists(marker) &&
                    int.TryParse(File.ReadAllText(marker),out var code)?code:null;
                SaveReport("UnexpectedExit",null,previous,exitCode);
                if(marker is not null && File.Exists(marker))File.Delete(marker);
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
            activityVersion++;
        }
    }
    public void PulseUi()=>Interlocked.Exchange(ref uiHeartbeat,DateTimeOffset.UtcNow.UtcTicks);
    public void Flush()
    {
        lock(gate)
        {
            if(closed || crashed) return;
            try
            {
                if(flushedVersion==activityVersion && File.Exists(pending) && DateTime.UtcNow-File.GetLastWriteTimeUtc(pending)<TimeSpan.FromSeconds(5))return;
                NamingGuideStore.WriteAtomic(pending,Snapshot(),false);flushedVersion=activityVersion;
            }
            catch { }
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
    private string Snapshot()
    {
        using var process=Process.GetCurrentProcess();
        var pulse=Interlocked.Read(ref uiHeartbeat);
        return JsonSerializer.Serialize(new { Version=2,StartedProcess=Environment.ProcessId,StartedAt=startedAt,
            CheckpointAt=DateTimeOffset.UtcNow,LastUiHeartbeat=pulse==0?(DateTimeOffset?)null:new DateTimeOffset(pulse,TimeSpan.Zero),
            AppVersion=System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),OS=Environment.OSVersion.VersionString,
            Runtime=Environment.Version.ToString(),MemoryBytes=process.WorkingSet64,PrivateBytes=process.PrivateMemorySize64,
            RecentActions=actions.ToArray() },StateStore.Json);
    }
    private void SaveReport(string reason,Exception? error,string recent,int? exitCode=null)
    {
        var exception=error is null?null:Regex.Replace(error.ToString(),@"AIza[\w-]{20,}|sk-[\w-]{12,}|(?i:(?:api[_ -]?key|authorization)\s*[=:]\s*(?:bearer\s+)?)[^\s,;]+","[redacted]");
        if(exception?.Length>24000) exception=exception[..24000];
        var path=Path.Combine(directory,"crash-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")[..6]+".json");
        NamingGuideStore.WriteAtomic(path,JsonSerializer.Serialize(new { At=DateTimeOffset.Now,Reason=reason,Exception=exception,
            ErrorType=error?.GetType().FullName,HResult=error is null?null:$"0x{error.HResult:X8}",ExitCode=exitCode,
            ExitCodeHex=exitCode is null?null:$"0x{exitCode:X8}",Session=JsonSerializer.Deserialize<JsonElement>(recent) },
            new JsonSerializerOptions(StateStore.Json) { WriteIndented=true }),false);
        PruneReports();
    }
    public int ClearReports()
    {
        lock(gate)
        {
            var count=0;
            foreach(var file in Directory.EnumerateFiles(directory,"crash-*.json")) {File.Delete(file);count++;}
            return count;
        }
    }
    private void PruneReports() {foreach(var old in Directory.EnumerateFiles(directory,"crash-*.json").OrderByDescending(File.GetLastWriteTimeUtc).Skip(10)) File.Delete(old);}
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
