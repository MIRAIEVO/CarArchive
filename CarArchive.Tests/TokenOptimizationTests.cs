using System.Net;
using System.Text.Json;
using CarArchive.Core;

static class TokenOptimizationTests
{
    const string TrustedUrl="https://manufacturer.example/archive/model";
    const string UntrustedUrl="https://unsupported.example/model";

    static void Check(bool value,string message="Token optimization assertion failed") { if(!value) throw new Exception(message); }

    public static void Register(Action<string,Func<Task>> test)
    {
        test("Token optimization preserves recognition images, identity fields, request budgets and calls in all modes",RecognitionPolicies);
        test("Token optimization narrows verification stages and deduplicates source context without weakening citations",VerificationPolicies);
        test("Token optimization rejects fabricated feature links and uncited supplemental evidence",RejectsUntrustedEvidence);
    }

    static async Task RecognitionPolicies()
    {
        Check(new AppSettings().OptimizeRecognitionTokens==false,"Optimization defaults off.");
        foreach(var mode in Enum.GetValues<RecognitionMode>())
        {
            var baseline=await CaptureRecognition(mode,false);
            var optimized=await CaptureRecognition(mode,true);
            var expectedImages=mode switch { RecognitionMode.Accurate=>8,RecognitionMode.Fast=>3,_=>1 };
            var expectedEdge=mode is RecognitionMode.FieldsOnly or RecognitionMode.Economy?1280:2048;
            Check(baseline.Bodies.Count==optimized.Bodies.Count && baseline.Bodies.Count==(mode==RecognitionMode.Accurate?2:1),"Policy toggle must not add or remove a recognition stage: "+mode);
            Check(baseline.Reservations==baseline.Bodies.Count && optimized.Reservations==optimized.Bodies.Count && baseline.Reservations==optimized.Reservations,"Policy toggle preserves reserved request count: "+mode);
            Check(baseline.Images.Edges.Count==optimized.Images.Edges.Count && baseline.Images.Edges.Count==(mode==RecognitionMode.Accurate?16:expectedImages),"Policy toggle preserves encoded-image count: "+mode);
            Check(baseline.Images.Edges.All(edge=>edge==expectedEdge) && optimized.Images.Edges.SequenceEqual(baseline.Images.Edges),"Policy toggle preserves image resolution: "+mode);
            Check(IdentityValues(baseline.Result.Identity).SequenceEqual(IdentityValues(optimized.Result.Identity)),"Policy toggle preserves all final identity fields: "+mode);
            Check(TextLength(optimized.Bodies)<TextLength(baseline.Bodies),"Optimized prompts are shorter: "+mode);
            for(var i=0;i<baseline.Bodies.Count;i++)
            {
                using var oldRequest=JsonDocument.Parse(baseline.Bodies[i]);using var newRequest=JsonDocument.Parse(optimized.Bodies[i]);
                var oldRoot=oldRequest.RootElement;var newRoot=newRequest.RootElement;
                var stageImages=ImageCount(oldRoot);
                Check(stageImages==(mode==RecognitionMode.Accurate?8:expectedImages),"The expected mode-specific view count is retained: "+mode);
                Check(oldRoot.GetProperty("store").GetBoolean()==newRoot.GetProperty("store").GetBoolean(),"Storage behavior is unchanged: "+mode);
                var generation=oldRoot.GetProperty("generation_config");
                Check(generation.GetRawText()==newRoot.GetProperty("generation_config").GetRawText(),"Thinking and output budgets are unchanged: "+mode);
                Check(generation.GetProperty("max_output_tokens").GetInt32()==(mode==RecognitionMode.Accurate?8192:4096)
                    && generation.GetProperty("thinking_level").GetString()==(mode==RecognitionMode.Accurate?"high":"low"),"Every recognition mode retains its required output and thinking budget.");
                Check(stageImages==ImageCount(newRoot),"Per-stage image count is unchanged: "+mode);
                var oldHasTools=oldRoot.TryGetProperty("tools",out var oldTools);var newHasTools=newRoot.TryGetProperty("tools",out var newTools);
                Check(oldHasTools==newHasTools && (!oldHasTools || oldTools.GetRawText()==newTools.GetRawText()),"Search tools are unchanged: "+mode);
                var schema=Schema(newRoot);var properties=schema.GetProperty("properties");
                if(mode==RecognitionMode.Accurate && i==1)
                    Check(properties.GetProperty("identityCorrections").GetProperty("items").GetProperty("properties").GetProperty("field").GetProperty("enum").GetArrayLength()==10,"Sparse corrections support every final identity field.");
                else
                {
                    var identityProperties=mode==RecognitionMode.FieldsOnly?properties:properties.GetProperty("identity").GetProperty("properties");
                    Check(identityProperties.EnumerateObject().Count()==10,"All ten final identity fields remain in the recognition schema.");
                }
                Check(!baseline.Bodies[i].Contains("NamingGuide",StringComparison.Ordinal) && !optimized.Bodies[i].Contains("NamingGuide",StringComparison.Ordinal)
                    && !baseline.Bodies[i].Contains("NamingSamples",StringComparison.Ordinal) && !optimized.Bodies[i].Contains("NamingSamples",StringComparison.Ordinal),"Local naming material is not uploaded.");
            }
        }
    }

    static async Task VerificationPolicies()
    {
        var baseline=await CaptureVerification(false,TrustedUrl,false);
        var optimized=await CaptureVerification(true,TrustedUrl,false);
        Check(baseline.Bodies.Count==2 && optimized.Bodies.Count==2 && baseline.Reservations==2 && optimized.Reservations==2,"Feature research and photo comparison retain the same two-call flow.");
        Check(baseline.Images.Edges.SequenceEqual(optimized.Images.Edges) && baseline.Images.Edges.Count==8 && baseline.Images.Edges.All(edge=>edge==2048),"Both policies send the same comparison views at the same resolution.");
        Check(baseline.Report.Status==VerificationStatus.Passed && optimized.Report.Status==VerificationStatus.Passed,"A real search citation and complete visible core checks remain sufficient.");
        var baselineSuggestion=baseline.Report.SuggestedIdentity;var optimizedSuggestion=optimized.Report.SuggestedIdentity;
        Check(baselineSuggestion is not null && optimizedSuggestion is not null
            && IdentityValues(baselineSuggestion).SequenceEqual(IdentityValues(optimizedSuggestion)),"Baseline and sparse-correction strategies produce compatible full identities.");
        Check(optimizedSuggestion is not null
            && IdentityValues(optimizedSuggestion).SequenceEqual(IdentityValues(Candidate("Series 500 Touring Special"))),"Sparse corrections merge into the complete candidate identity.");
        Check(TextLength(optimized.Bodies)<TextLength(baseline.Bodies),"Optimized verification prompts and data are shorter.");
        for(var i=0;i<baseline.Bodies.Count;i++)
        {
            using var oldRequest=JsonDocument.Parse(baseline.Bodies[i]);using var newRequest=JsonDocument.Parse(optimized.Bodies[i]);
            var oldRoot=oldRequest.RootElement;var newRoot=newRequest.RootElement;
            Check(ImageCount(oldRoot)==(i==0?0:8) && ImageCount(oldRoot)==ImageCount(newRoot),"Verification retains stage-specific image counts.");
            var generation=oldRoot.GetProperty("generation_config");
            Check(generation.GetRawText()==newRoot.GetProperty("generation_config").GetRawText()
                && generation.GetProperty("max_output_tokens").GetInt32()==8192 && generation.GetProperty("thinking_level").GetString()=="high",
                "Verification thinking and output budgets are unchanged.");
            var oldHasTools=oldRoot.TryGetProperty("tools",out var oldTools);var newHasTools=newRoot.TryGetProperty("tools",out var newTools);
            Check(oldHasTools==newHasTools && (!oldHasTools || oldTools.GetRawText()==newTools.GetRawText()),"Verification search stages are unchanged.");
        }
        using var baselineResearch=JsonDocument.Parse(baseline.Bodies[0]);using var optimizedResearch=JsonDocument.Parse(optimized.Bodies[0]);
        using var baselineCompare=JsonDocument.Parse(baseline.Bodies[1]);using var optimizedCompare=JsonDocument.Parse(optimized.Bodies[1]);
        var baselineResearchSchema=Schema(baselineResearch.RootElement);var optimizedResearchSchema=Schema(optimizedResearch.RootElement);
        var baselineCompareSchema=Schema(baselineCompare.RootElement);var optimizedCompareSchema=Schema(optimizedCompare.RootElement);
        Check(baselineResearchSchema.GetProperty("properties").EnumerateObject().Count()==7
            && optimizedResearchSchema.GetProperty("properties").EnumerateObject().Count()==4
            && !optimizedResearchSchema.GetProperty("properties").TryGetProperty("identity",out _),"Feature research no longer echoes the supplied candidate.");
        Check(baselineCompareSchema.GetProperty("properties").EnumerateObject().Count()==9
            && optimizedCompareSchema.GetProperty("properties").EnumerateObject().Count()==8
            && !optimizedCompareSchema.GetProperty("properties").TryGetProperty("identity",out _),"Photo comparison returns sparse corrections instead of repeating all identity fields.");
        var oldInput=TextInput(baselineCompare.RootElement);var newInput=TextInput(optimizedCompare.RootElement);
        Check(Count(oldInput,TrustedUrl)>Count(newInput,TrustedUrl) && Count(newInput,TrustedUrl)==1,"Repeated citation URLs are represented once in optimized stage context.");
        Check(!baseline.Bodies.Concat(optimized.Bodies).Any(body=>body.Contains("LocalNamingRules",StringComparison.Ordinal)
            || body.Contains("NamingSamples",StringComparison.Ordinal)),"No naming guide or learning material is uploaded.");
    }

    static async Task RejectsUntrustedEvidence()
    {
        var legacyForged=await CaptureVerification(false,UntrustedUrl,false);
        var forged=await CaptureVerification(true,UntrustedUrl,false);
        Check(legacyForged.Report.Status!=VerificationStatus.Passed && forged.Report.Status!=VerificationStatus.Passed
            && forged.Report.Stages.Count==2,"Neither strategy accepts a feature URL without a matching provider citation.");
        var supplement=await CaptureUncitedSupplement();
        Check(supplement.Report.Stages.Count==3 && supplement.Reservations==3 && supplement.Report.Status==VerificationStatus.Incomplete,
            "An uncited supplemental response cannot upgrade verification to passed.");
    }

    static async Task<RecognitionCapture> CaptureRecognition(RecognitionMode mode,bool optimized)
    {
        var images=new Images();var bodies=new List<string>();var reservations=0;
        using var handler=new Handler(async (request,number)=>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync());
            var first=mode!=RecognitionMode.Accurate || number==1;
            var output=mode==RecognitionMode.FieldsOnly?JsonSerializer.Serialize(Candidate(),StateStore.Json)
                :first?RecognitionJson(mode==RecognitionMode.Accurate):ComparisonJson(optimized);
            return Reply(output,first,first,TrustedUrl);
        });
        using var http=new HttpClient(handler);
        var service=new GeminiService(http,images,()=>"intercepted-test-key",()=>reservations++);
        var photos=Enumerable.Range(0,8).Select(i=>new Photo { Path="synthetic-"+i+".jpg" }).ToArray();
        var settings=new AppSettings { Search=SearchPreference.Grounded, Mode=mode,Model="gemini-3.8-flash",OptimizeRecognitionTokens=optimized };
        var result=await service.RecognizeAsync(photos,settings,false,default);
        return new(bodies,images,reservations,result);
    }

    static async Task<VerificationCapture> CaptureVerification(bool optimized,string featureUrl,bool supplement)
    {
        var images=new Images();var bodies=new List<string>();var reservations=0;
        using var handler=new Handler(async (request,number)=>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync());
            if(number==1)
            {
                var output=optimized?FeatureResearchJson(featureUrl):JsonSerializer.Serialize(Research(featureUrl),StateStore.Json);
                return Reply(output,true,true,TrustedUrl,true);
            }
            return Reply(ComparisonJson(optimized,supplement),false,false,TrustedUrl);
        });
        using var http=new HttpClient(handler);var service=new GeminiService(http,images,()=>"intercepted-test-key",()=>reservations++);
        var settings=new AppSettings { Search=SearchPreference.Grounded, Mode=RecognitionMode.Accurate,Model="gemini-3.8-flash",OptimizeRecognitionTokens=optimized };
        var report=await service.VerifyAsync(Enumerable.Range(0,8).Select(i=>new Photo { Path="synthetic-"+i+".jpg" }).ToArray(),settings,Candidate(),"",default);
        return new(bodies,images,reservations,report);
    }

    static async Task<VerificationCapture> CaptureUncitedSupplement()
    {
        var images=new Images();var bodies=new List<string>();var reservations=0;
        using var handler=new Handler(async (request,number)=>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync());
            if(number==1) return Reply(FeatureResearchJson(UntrustedUrl),true,true,TrustedUrl);
            if(number==2) return Reply(ComparisonJson(true,true),false,false,TrustedUrl);
            return Reply(SupplementJson(UntrustedUrl),true,false,TrustedUrl);
        });
        using var http=new HttpClient(handler);var service=new GeminiService(http,images,()=>"intercepted-test-key",()=>reservations++);
        var report=await service.VerifyAsync([new Photo { Path="synthetic.jpg" }],new AppSettings { Search=SearchPreference.Grounded, Mode=RecognitionMode.Economy,Model="gemini-3.8-flash",OptimizeRecognitionTokens=true },Candidate(),"",default);
        return new(bodies,images,reservations,report);
    }

    static RecognitionResult Research(string url)=>new() {
        Identity=Candidate(),Features=Features(url).ToList(),Research="Manufacturer archive describes the generation.",Uncertainties="",Conflict=false
    };

    static string RecognitionJson(bool withFeatures)
    {
        if(withFeatures) return JsonSerializer.Serialize(new {
            identity=Candidate(),observations="Visible lamps match the photographed car.",research="Manufacturer archive confirms the generation.",
            uncertainties="",alternatives=Array.Empty<string>(),conflict=false,features=Features(TrustedUrl)
        },StateStore.Json);
        return JsonSerializer.Serialize(new {
            identity=Candidate(),observations="Visible lamps match the photographed car.",research="Manufacturer archive confirms the generation.",
            uncertainties="",alternatives=Array.Empty<string>(),conflict=false
        },StateStore.Json);
    }

    static string FeatureResearchJson(string url)=>JsonSerializer.Serialize(new {
        research="Manufacturer archive describes the generation.",uncertainties="",conflict=false,features=Features(url)
    },StateStore.Json);

    static string ComparisonJson(bool optimized,bool askSupplement=false)
    {
        var checks=Checks();
        if(optimized) return JsonSerializer.Serialize(new {
            identityCorrections=new[] { new { field="variant",value="Series 500 Touring Special" } },
            observations="Visible lamps match.",uncertainties="",conflict=false,summary="Core features match.",
            configVerified=true,supplementQuery=askSupplement?"research exact model generation production range":"",checks
        });
        return JsonSerializer.Serialize(new RecognitionResult {
            Identity=Candidate("Series 500 Touring Special"),Observations="Visible lamps match.",Uncertainties="",Conflict=false,Summary="Core features match.",
            ConfigVerified=true,SupplementQuery=askSupplement?"research exact model generation production range":"",Checks=checks.ToList()
        },StateStore.Json);
    }

    static string SupplementJson(string url)=>JsonSerializer.Serialize(new {
        identityCorrections=Array.Empty<object>(),observations="The photograph does not show the missing detail.",uncertainties="",
        conflict=false,summary="The reference could not be verified.",configVerified=false,supplementQuery="",
        checks=Checks(),features=Features(url)
    },StateStore.Json);

    static ReferenceFeature[] Features(string url)=>new[] {
        new ReferenceFeature { Id="series",Field="series",Expected="Distinctive headlight design",Critical=true,ReferenceUrl=url },
        new ReferenceFeature { Id="generation",Field="generation",Expected="Generation-specific rear light design",Critical=true,ReferenceUrl=url },
        new ReferenceFeature { Id="phase",Field="phase",Expected="Facelift bumper detail",Critical=true,ReferenceUrl=url }
    };

    static FeatureComparison[] Checks()=>new[] {
        new FeatureComparison { FeatureId="series",Match="match",Observation="The headlight shape matches." },
        new FeatureComparison { FeatureId="generation",Match="match",Observation="The rear light shape matches." },
        new FeatureComparison { FeatureId="phase",Match="match",Observation="The bumper detail matches." }
    };

    static CarIdentity Candidate(string variant="Series 500 Touring")=>new() {
        Brand="Example",Series="Series",Variant=variant,Body="Touring",OfficialName="Example Series 500 Touring",
        Generation="3rd",Code="X3",Phase="Facelift",Package="Premium",ProductionYears="2020-2024"
    };

    static string[] IdentityValues(CarIdentity identity)=>[
        identity.Brand,identity.Series,identity.Variant,identity.Body,identity.OfficialName,
        identity.Generation,identity.Code,identity.Phase,identity.Package,identity.ProductionYears
    ];

    static HttpResponseMessage Reply(string output,bool search,bool cited,string citationUrl,bool duplicateCitation=false)
    {
        var steps=new List<object>();
        if(search) steps.Add(new { type="google_search_call",arguments=new { queries=new[]{"manufacturer model generation distinguishing features"} } });
        var annotations=cited
            ?duplicateCitation?new object[]{Citation(citationUrl),Citation(citationUrl)}:new object[]{Citation(citationUrl)}
            :Array.Empty<object>();
        steps.Add(new { type="model_output",content=new[]{new { type="text",text=output,annotations }} });
        return new(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(new { status="completed",steps,usage=new { total_input_tokens=90,total_output_tokens=45,total_thought_tokens=12 } })) };
    }

    static object Citation(string url)=>new { type="url_citation",url,title="Manufacturer archive",start_index=0,end_index=0 };

    static JsonElement Schema(JsonElement request)=>request.GetProperty("response_format").GetProperty("schema");

    static int ImageCount(JsonElement request)=>request.GetProperty("input").EnumerateArray().Count(item=>item.GetProperty("type").GetString()=="image");

    static string TextInput(JsonElement request)=>string.Join("\n",request.GetProperty("input").EnumerateArray()
        .Where(item=>item.GetProperty("type").GetString()=="text").Select(item=>item.GetProperty("text").GetString()));

    static int TextLength(IEnumerable<string> bodies)=>bodies.Sum(body=>{
        using var request=JsonDocument.Parse(body);
        return request.RootElement.GetProperty("input").EnumerateArray().Where(item=>item.GetProperty("type").GetString()=="text")
            .Sum(item=>(item.GetProperty("text").GetString()??"").Length);
    });

    static int Count(string text,string value)=>text.Split(value,StringSplitOptions.None).Length-1;

    sealed record RecognitionCapture(List<string> Bodies,Images Images,int Reservations,RecognitionResult Result);
    sealed record VerificationCapture(List<string> Bodies,Images Images,int Reservations,VerificationReport Report);

    sealed class Handler(Func<HttpRequestMessage,int,Task<HttpResponseMessage>> respond):HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>respond(request,++Count);
    }

    sealed class Images:IPhotoService
    {
        public List<int> Edges { get; }=[];
        public Task<Photo> ImportAsync(string path,CancellationToken token)=>Task.FromResult(new Photo { Path=path });
        public Task<byte[]> AnalysisJpegAsync(Photo photo,int longestEdge,CancellationToken token)
        {
            Edges.Add(longestEdge);
            return Task.FromResult(new byte[]{1,2,3});
        }
    }
}
