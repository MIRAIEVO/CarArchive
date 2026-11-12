using System.Net;
using System.Text.Json;
using CarArchive.Core;

static class VerificationTests
{
    const string Url="https://manufacturer.example/press/model";
    static void Check(bool value,string message="Verification assertion failed") { if(!value) throw new Exception(message); }
    static CarIdentity Candidate()=>new() { Brand="Example",Series="Series",Variant="Series 500 Coupe",Generation="3rd",Code="X3",Phase="Facelift" };
    static List<ReferenceFeature> Features(string url=Url)=>new[]{"series","generation","phase"}.Select((s,i)=>new ReferenceFeature { Id=s,Field=s,Expected=$"Visible distinguishing {s} characteristic",Critical=true,ReferenceUrl=url }).ToList();
    static RecognitionResult Research(string url=Url)=>new() { Identity=Candidate(),Features=Features(url),Research="有据的辨识特征" };
    static RecognitionResult Compare(string match="match",string query="")=>new() { Identity=Candidate(),Summary="逐项比对完成",SupplementQuery=query,Checks=new[]{"series","generation","phase"}.Select(s=>new FeatureComparison { FeatureId=s,Match=match,Observation="照片中的灯组与参考特征对照" }).ToList() };
    static HttpResponseMessage Envelope(RecognitionResult result,bool search=true,bool cited=true,bool incomplete=false)
    {
        var steps=new List<object>();
        if(search) steps.Add(new { type="google_search_call",arguments=new { queries=new[]{"model distinguishing headlights facelift"} } });
        steps.Add(new { type="model_output",content=new[]{new { type="text",text=JsonSerializer.Serialize(result),annotations=cited?new object[]{new { type="url_citation",url=Url,title="Official archive",start_index=0,end_index=0 }}:Array.Empty<object>() }} });
        return new(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(new { status=incomplete?"incomplete":"completed",steps,usage=new { total_input_tokens=100,total_output_tokens=40,total_thought_tokens=10 } })) };
    }
    sealed class Handler(Func<HttpRequestMessage,int,CancellationToken,HttpResponseMessage> run):HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(run(request,++Count,token));
    }
    sealed class Images:IPhotoService
    {
        public List<(Photo Photo,int Edge)> Calls=[];
        public Task<Photo> ImportAsync(string path,CancellationToken token)=>throw new NotSupportedException();
        public Task<byte[]> AnalysisJpegAsync(Photo photo,int longestEdge,CancellationToken token) { Calls.Add((photo,longestEdge));return Task.FromResult(new byte[]{1,2,3}); }
    }
    public static void Register(Action<string,Func<Task>> test)
    {
        test("Deep verification: searched references without images then cropped comparison without search; series-only pass",async ()=>
        {
            var images=new Images();var crop=new Photo { Crop=new(.2,.2,.5,.5) };
            using var handler=new Handler((request,n,token)=> {
                using var json=JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());var root=json.RootElement;
                var count=root.GetProperty("input").EnumerateArray().Count(x=>x.GetProperty("type").GetString()=="image");
                Check(n==1?count==0 && root.TryGetProperty("tools",out _):count==1 && !root.TryGetProperty("tools",out _));
                var result=n==1?Research():Compare();result.ConfigVerified=true;return Envelope(result,n==1,n==1);
            });
            using var http=new HttpClient(handler);var service=new GeminiService(http,images,()=>"fake",()=>{});
            var report=await service.VerifyAsync([crop,new Photo()],new() { Mode=RecognitionMode.Accurate },Candidate(),"核对中改",default);
            Check(handler.Count==2 && report.Status==VerificationStatus.Passed && !report.ConfigVerified && report.StatusText.Contains("配置未证实"));
            Check(report.InputTokens==200 && report.OutputTokens==80 && report.ThinkingTokens==20 && report.SearchCount==1);
            Check(images.Calls.Count==1 && ReferenceEquals(images.Calls[0].Photo,crop) && images.Calls[0].Edge==1280);
        });
        test("Deep verification: contradiction fails; invisible critical features never pass or trigger pointless supplement",async ()=>
        {
            foreach(var (match,expected) in new[]{("contradiction",VerificationStatus.Failed),("not_visible",VerificationStatus.Insufficient)})
            {
                using var h=new Handler((_,n,_)=>Envelope(n==1?Research():Compare(match,"research a hidden badge on this specific car"),n==1,n==1));
                using var http=new HttpClient(h);var report=await new GeminiService(http,new Images(),()=>"fake",()=>{}).VerifyAsync([new()],new(),Candidate(),"",default);
                Check(report.Status==expected && h.Count==2);
            }
        });
        test("Deep verification: fabricated feature links, missing searches, and conflicts cannot pass",async ()=>
        {
            for(var scenario=0;scenario<3;scenario++)
            {
                using var h=new Handler((_,n,_)=> { var result=n==1?Research(scenario==0?"https://fake.example/unsupported":Url):Compare();if(scenario==2 && n==1) result.Conflict=true;return Envelope(result,n==1 && scenario!=1,n==1); });
                using var http=new HttpClient(h);var report=await new GeminiService(http,new Images(),()=>"fake",()=>{}).VerifyAsync([new()],new(),Candidate(),"",default);
                Check(report.Status==(scenario==1?VerificationStatus.Incomplete:VerificationStatus.Insufficient) && h.Count==2);
            }
        });
        test("Deep verification: actionable ungrounded reference permits one targeted supplement, never a fourth request",async ()=>
        {
            using var h=new Handler((request,n,token)=> {
                using var json=JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());Check(n<=3);
                if(n==3) { Check(json.RootElement.TryGetProperty("tools",out _));var final=Compare();final.Features=Features();return Envelope(final); }
                return Envelope(n==1?Research("https://fake.example/uncited"):Compare("match","official model facelift headlamp difference"),n==1,n==1);
            });
            using var http=new HttpClient(h);var report=await new GeminiService(http,new Images(),()=>"fake",()=>{}).VerifyAsync([new()],new(),Candidate(),"",default);
            Check(h.Count==3 && report.Status==VerificationStatus.Passed && report.Stages.Count==3);
        });
        test("Deep verification: stage error retains original candidate, completed evidence and usage without retry",async ()=>
        {
            using var h=new Handler((_,n,_)=>n==1?Envelope(Research()):new(HttpStatusCode.TooManyRequests) { Content=new StringContent("secret raw server output") });
            using var http=new HttpClient(h);var candidate=Candidate();var report=await new GeminiService(http,new Images(),()=>"fake",()=>{}).VerifyAsync([new()],new(),candidate,"",default);
            Check(h.Count==2 && report.Status==VerificationStatus.Incomplete && report.InputTokens==100 && report.Sources.Count==1);
            Check(candidate.Variant=="Series 500 Coupe" && !report.Error.Contains("secret"));
        });
        test("Deep verification: supplement without its own real search cannot upgrade evidence to passed",async ()=>
        {
            using var h=new Handler((_,n,_)=> {
                if(n==1) return Envelope(Research("https://fake.example/uncited"));
                var result=Compare("match","official model facelift headlamp difference");
                if(n==3) result.Features=Features();
                return Envelope(result,false,false);
            });
            using var http=new HttpClient(h);var report=await new GeminiService(http,new Images(),()=>"fake",()=>{}).VerifyAsync([new()],new(),Candidate(),"",default);
            Check(h.Count==3 && report.Status==VerificationStatus.Incomplete);
        });
        test("Deep verification: canceled and truncated stages retain prior evidence and truncation token usage",async ()=>
        {
            foreach(var canceled in new[]{true,false})
            {
                using var h=new Handler((_,n,_)=> { if(n==1) return Envelope(Research());if(canceled) throw new OperationCanceledException();return Envelope(Compare(),false,false,true); });
                using var http=new HttpClient(h);var report=await new GeminiService(http,new Images(),()=>"fake",()=>{}).VerifyAsync([new()],new(),Candidate(),"",default);
                Check(report.Status==(canceled?VerificationStatus.Canceled:VerificationStatus.Incomplete) && report.Stages.Count==2);
                Check(report.InputTokens==(canceled?100:200) && report.Sources.Count==1);
            }
        });
        test("Deep verification: absent candidate and unsupported image-search setting issue no API request",async ()=>
        {
            using var h=new Handler((_,_,_)=>throw new Exception("Unexpected request"));using var http=new HttpClient(h);var backend=new GeminiService(http,new Images(),()=>"fake",()=>{});
            foreach(var (candidate,settings) in new[]{(new CarIdentity(),new AppSettings()),(Candidate(),new AppSettings { Search=SearchPreference.ImageReference })})
            { var thrown=false;try { await backend.VerifyAsync([new()],settings,candidate,"",default); } catch(InvalidOperationException) { thrown=true; }Check(thrown && h.Count==0); }
        });
        test("Deep verification: null or duplicate feature payloads and contradictory identity cannot produce a pass",async ()=>
        {
            for(var scenario=0;scenario<5;scenario++)
            {
                using var h=new Handler((_,n,_)=> {
                    var result=n==1?Research():Compare();
                    if(n==1 && scenario==0) result.Features=null!;
                    if(n==1 && scenario==1) result.Features.Add(result.Features[0]);
                    if(n==2 && scenario==2) result.Identity.Series="Completely different car";
                    if(n==2 && scenario==3) result.Checks[0].Match="maybe";
                    if(n==2 && scenario==4) { result.Identity.Series="Corrected car";result.Checks[0].Match="contradiction"; }
                    return Envelope(result,n==1,n==1);
                });
                using var http=new HttpClient(h);var report=await new GeminiService(http,new Images(),()=>"fake",()=>{}).VerifyAsync([new()],new(),Candidate(),"",default);
                Check(report.Status==(scenario==0?VerificationStatus.Incomplete:scenario==4?VerificationStatus.Failed:VerificationStatus.Insufficient));
            }
        });
        test("Recognition profiles: view limits, token budgets and accurate comparison are distinct; no local naming material transmitted",async ()=>
        {
            foreach(var mode in Enum.GetValues<RecognitionMode>())
            {
                var expected=mode switch { RecognitionMode.Accurate=>8,RecognitionMode.Fast=>3,_=>1 };
                using var h=new Handler((request,n,token)=> {
                    var text=request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();using var json=JsonDocument.Parse(text);var root=json.RootElement;
                    Check(root.GetProperty("input").EnumerateArray().Count(x=>x.GetProperty("type").GetString()=="image")==expected);
                    Check(root.GetProperty("generation_config").GetProperty("max_output_tokens").GetInt32()==(mode==RecognitionMode.Accurate?8192:4096));
                    Check(!text.Contains("NamingGuide") && !text.Contains("BrandFolders"));
                    return Envelope(n==1?Research():Compare(),n==1,n==1);
                });
                using var http=new HttpClient(h);var result=await new GeminiService(http,new Images(),()=>"fake",()=>{}).RecognizeAsync(Enumerable.Range(0,12).Select(_=>new Photo()).ToArray(),new() { Mode=mode },false,default);
                Check(h.Count==(mode==RecognitionMode.Accurate?2:1));Check(mode!=RecognitionMode.Accurate || result.Verification?.Status==VerificationStatus.Passed);
            }
        });
    }
}
