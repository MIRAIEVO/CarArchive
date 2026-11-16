#if DEBUG
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarArchive.Core;
using CarArchive.Services;
using Microsoft.Data.Sqlite;

namespace CarArchive;
public sealed partial class MainPage
{
    private sealed class TokenAbCase
    {
        public string Name { get; set; }="";
        public string[] PhotoPaths { get; set; }=[];
        public CarIdentity Expected { get; set; }=new();
    }
    private sealed class TokenAbRun
    {
        public string Case { get; set; }="";
        public RecognitionMode Mode { get; set; }
        public bool Optimized { get; set; }
        public int Requests { get; set; }
        public CarIdentity? Identity { get; set; }
        public Dictionary<string,bool> KnownFieldMatches { get; set; }=[];
        public long? InputTokens { get; set; }
        public long? OutputTokens { get; set; }
        public long? ThoughtTokens { get; set; }
        public long? TotalTokens { get; set; }
        public int? SearchCount { get; set; }
        public int Sources { get; set; }
        public string Verification { get; set; }="";
        public string Error { get; set; }="";
    }
    private async Task RunTokenAbVerificationAsync(string directory)
    {
        directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);var output=Path.Combine(directory,"token-ab-verification.json");var runs=new List<TokenAbRun>();var originalHashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var calls=0;var runCalls=0;bool? originalsUnchanged=null;string status="Starting";string? error=null;string? current=null;
        void Save()=>File.WriteAllText(output,JsonSerializer.Serialize(new { Status=status,Error=error,Current=current,RealApiCallsUsed=calls,RequestsInThisRun=runCalls,Limit=40,OriginalsUnchanged=originalsUnchanged,Runs=runs,Comparison=TokenAbSummary(runs),CompletedUtc=DateTimeOffset.UtcNow },new JsonSerializerOptions(StateStore.Json) { WriteIndented=true }));
        try
        {
            if(!ViewModels.MainViewModel.DataDirectory.StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || VM.Groups.Count!=0) throw new InvalidOperationException("Token A/B requires a fresh isolated workspace.");
            var manifest=Environment.GetEnvironmentVariable("CARARCHIVE_AB_CASES") ?? throw new InvalidOperationException("An explicit confirmed-case manifest is required.");
            var source=Environment.GetEnvironmentVariable("CARARCHIVE_LIVE_SOURCE_DATA") ?? throw new InvalidOperationException("An explicit read-only source data directory is required.");
            var cases=JsonSerializer.Deserialize<TokenAbCase[]>(File.ReadAllText(manifest),StateStore.Json) ?? [];
            if(cases.Length!=3 || cases.Any(c=>c.Name.Length==0 || c.PhotoPaths.Length==0 || c.PhotoPaths.Length>8)) throw new InvalidDataException("Exactly three labeled cases with one to eight photographs are required.");
            var budget=Environment.GetEnvironmentVariable("CARARCHIVE_AB_BUDGET") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifest))!,"budget.json");Directory.CreateDirectory(Path.GetDirectoryName(budget)!);
            var lockName="Local\\CarArchive-TokenAB-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(budget).ToUpperInvariant())));
            using var lease=new Mutex(false,lockName);bool held;try { held=lease.WaitOne(0); }catch(AbandonedMutexException) { held=true; }if(!held) throw new IOException("Another token A/B run owns this budget.");
            try
            {
                if(File.Exists(budget)) { using var saved=JsonDocument.Parse(File.ReadAllText(budget));if(saved.RootElement.TryGetProperty("Used",out var used)) calls=used.GetInt32(); }
                if(calls<0 || calls>=40) throw new InvalidOperationException("The persistent forty-call validation budget is exhausted.");
                var settings=new AppSettings();using(var db=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=Path.Combine(source,"cararchive.db"),Mode=SqliteOpenMode.ReadOnly,Pooling=false }.ToString()))
                { db.Open();using var query=db.CreateCommand();query.CommandText="SELECT json FROM workspace WHERE id=1";if(query.ExecuteScalar() is string json) { using var doc=JsonDocument.Parse(json);if(doc.RootElement.TryGetProperty("Settings",out var s)) settings=s.Deserialize<AppSettings>(StateStore.Json) ?? settings; } }
                var key=new CredentialService(source).Read();if(key.Length==0) throw new InvalidOperationException("No credential is available for the current Windows user.");
                using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(25));using var http=new HttpClient { Timeout=TimeSpan.FromMinutes(3) };
                var service=new GeminiService(http,VM.Images,()=>key,()=>
                {
                    if(calls>=40) throw new InvalidOperationException("Persistent token A/B call limit reached.");calls++;runCalls++;
                    var temp=budget+".tmp";using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) { JsonSerializer.Serialize(stream,new { Used=calls,Limit=40,UpdatedUtc=DateTimeOffset.UtcNow });stream.Flush(true); }File.Move(temp,budget,true);status="Running";Save();
                });
                foreach(var item in cases)
                {
                    var photos=new List<Photo>();for(var i=0;i<item.PhotoPaths.Length;i++)
                    {
                        var original=Path.GetFullPath(item.PhotoPaths[i]);originalHashes[original]=await ArchiveService.HashAsync(original,timeout.Token);
                        var folder=Path.Combine(directory,"samples",photos.Count.ToString()+"-"+runs.Count);Directory.CreateDirectory(folder);var copy=Path.Combine(folder,Path.GetFileName(original));File.Copy(original,copy,false);
                        if(await ArchiveService.HashAsync(copy,timeout.Token)!=originalHashes[original]) throw new IOException("A fixture copy did not match its original.");photos.Add(await VM.Images.ImportAsync(copy,timeout.Token));
                    }
                    foreach(var mode in new[]{RecognitionMode.FieldsOnly,RecognitionMode.Economy,RecognitionMode.Fast,RecognitionMode.Accurate})
                    {
                    foreach(var optimized in new[]{false,true})
                    {
                        if(calls>=40) { status="BudgetReached";break; }
                        current=item.Name+" / "+mode+" / "+(optimized?"optimized":"compatible");var start=calls;settings.Mode=mode;settings.OptimizeRecognitionTokens=optimized;var run=new TokenAbRun { Case=item.Name,Mode=mode,Optimized=optimized };runs.Add(run);Save();
                        try
                        {
                            var result=await service.RecognizeAsync(photos,settings,false,timeout.Token);run.Identity=result.Identity.Clone();run.KnownFieldMatches=GradeTokenAb(item.Expected,result.Identity);
                            var stages=result.Verification is { Stages.Count:>0 } verification?verification.Stages.Select(s=>s.Result).ToArray():[result];
                            run.InputTokens=SumUsage(stages,"total_input_tokens");run.OutputTokens=SumUsage(stages,"total_output_tokens");run.ThoughtTokens=SumUsage(stages,"total_thought_tokens");run.TotalTokens=SumUsage(stages,"total_tokens");
                            run.SearchCount=stages.Where(s=>s.ActualSearch!="未返回搜索记录").All(s=>s.SearchCount.HasValue)?stages.Where(s=>s.SearchCount.HasValue).Sum(s=>s.SearchCount!.Value):null;
                            run.Sources=result.Verification?.Sources.Count ?? result.Sources.Count;run.Verification=result.Verification?.StatusText ?? (result.Grounded?"已返回真实联网依据":"未完成查证");run.Error=result.Verification?.Error ?? "";
                        }
                        catch(Exception e) { run.Error=e.Message;status="Incomplete";error=e.Message; }
                        run.Requests=calls-start;Save();
                        if(run.Error.Contains("HTTP 401") || run.Error.Contains("HTTP 403") || run.Error.Contains("HTTP 404") || run.Error.Contains("HTTP 429") || status=="Incomplete") { status="Stopped";break; }
                    }
                    if(status is "Stopped" or "BudgetReached" || calls>=40) break;
                    }
                    if(status is "Stopped" or "BudgetReached" || calls>=40) break;
                }
                if(status is not ("Stopped" or "BudgetReached")) status=runs.Count==24?"Completed":"Incomplete";
                originalsUnchanged=true;foreach(var pair in originalHashes) if(!File.Exists(pair.Key) || await ArchiveService.HashAsync(pair.Key)!=pair.Value) originalsUnchanged=false;
                Save();App.MainWindow!.Title="CarArchive 1.1.5 · Token A/B 完成";
            }
            finally { lease.ReleaseMutex(); }
        }
        catch(Exception e) { status="Failed";error=e is System.Security.Cryptography.CryptographicException?"The saved credential belongs to another Windows user.":e.Message;Save();VM.Status="Token A/B 未完成，请查看隔离报告。"; }
    }
    private static long? SumUsage(IEnumerable<RecognitionResult> results,string field)
    {
        long sum=0;foreach(var result in results) { if(result.RawUsage.Length==0) return null;using var doc=JsonDocument.Parse(result.RawUsage);if(!doc.RootElement.TryGetProperty(field,out var value) || !value.TryGetInt64(out var n)) return null;sum+=n; }return sum;
    }
    private static Dictionary<string,bool> GradeTokenAb(CarIdentity expected,CarIdentity actual)
    {
        static string Key(string value)=>string.Concat(LatinText.English(value).ToLowerInvariant().Where(char.IsLetterOrDigit));
        static string Brand(string value)=>Key(value) switch { "benz" or "mercedes"=>"mercedesbenz",var s=>s };
        static string Series(string value)=>Key(value).Replace("class","") switch { "ml"=>"m",var s=>s };
        static string Phase(string value)=>Key(value) switch { "1stfacelift" or "lci"=>"facelift",var s=>s };
        var grades=new Dictionary<string,bool>();
        void Add(string name,string e,string a,Func<string,string> normalize) { if(!string.IsNullOrWhiteSpace(e)) grades[name]=normalize(e)==normalize(a); }
        Add("Brand",expected.Brand,actual.Brand,Brand);Add("Series",expected.Series,actual.Series,Series);Add("Generation",expected.Generation,actual.Generation,NamingText.Generation);Add("Code",expected.Code,actual.Code,Key);Add("Phase",expected.Phase,actual.Phase,Phase);return grades;
    }
    private static object TokenAbSummary(IReadOnlyList<TokenAbRun> runs)
    {
        var pairs=runs.GroupBy(r=>(r.Case,r.Mode)).Where(g=>g.Any(r=>!r.Optimized) && g.Any(r=>r.Optimized)).Select(g=>new { Old=g.First(r=>!r.Optimized),New=g.First(r=>r.Optimized) }).ToArray();
        var errors=pairs.Any(p=>p.Old.Error.Length>0 || p.New.Error.Length>0);var changed=pairs.SelectMany(p=>p.Old.KnownFieldMatches.Where(x=>x.Value && !p.New.KnownFieldMatches.GetValueOrDefault(x.Key)).Select(x=>p.Old.Case+" / "+p.Old.Mode+" / "+x.Key)).ToArray();
        var complete=pairs.Length==12 && pairs.All(p=>p.Old.Identity is not null && p.New.Identity is not null);var totalsKnown=complete && pairs.All(p=>p.Old.TotalTokens.HasValue && p.New.TotalTokens.HasValue);
        return new { CompletePairs=pairs.Length,NoObservedKnownFieldRegression=complete && !errors && changed.Length==0,RegressedKnownFields=changed,CompatibleTotalTokens=totalsKnown?pairs.Sum(p=>p.Old.TotalTokens!.Value):(long?)null,OptimizedTotalTokens=totalsKnown?pairs.Sum(p=>p.New.TotalTokens!.Value):(long?)null,Note="Only supplied confirmed fields are graded. Unknown usage remains null; no finite sample guarantees zero regression. The application default is not changed by this probe." };
    }
}
#endif
