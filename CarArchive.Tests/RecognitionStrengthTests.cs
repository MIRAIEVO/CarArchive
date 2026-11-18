using System.Net;
using System.Text.Json;
using CarArchive.Core;

static class RecognitionStrengthTests
{
    public static void Register(Action<string,Func<Task>> test)
    {
        test("OpenAI High SSE exhaustion recovers once with lower effort",()=>StreamRecoveryAsync(false));
        test("OpenAI interrupted stream does not resubmit",()=>StreamRecoveryAsync(true));
        foreach(var provider in Enum.GetValues<RecognitionProvider>())
            foreach(var online in new[]{false,true})
                test($"{provider} planned recognition, search={online}",async ()=>{
                    foreach(var strength in Enum.GetValues<RecognitionStrength>())
                        await CheckAsync(provider,online,strength);
                });
        test("Ultra cancels without adopting first-round evidence",async ()=>{
            using var cancel=new CancellationTokenSource();
            try {await CheckAsync(RecognitionProvider.OpenAI,true,RecognitionStrength.Ultra,cancel:cancel);}
            catch(OperationCanceledException){return;}
            throw new Exception("Canceled result was adopted.");
        });
        test("Ultra retains valid first pass if follow-up is rate limited",()=>CheckAsync(RecognitionProvider.OpenAI,true,RecognitionStrength.Ultra,rateLimit:true));
        test("Legacy strength resolves without altering stored mode or provider preferences",()=>{
            Require(RecognitionPlan.FromLegacy(RecognitionMode.FieldsOnly)==RecognitionStrength.Low);
            Require(RecognitionPlan.FromLegacy(RecognitionMode.Accurate)==RecognitionStrength.Max);
            Require(RecognitionPlan.FromLegacy(RecognitionMode.Fast)==RecognitionStrength.Medium);
            Require(RecognitionPlan.FromLegacy(RecognitionMode.Economy)==RecognitionStrength.Minimal);
            Require(RecognitionPlan.DisplayLabel("Gemini识别 · 进行中")=="AI识别 · 进行中");
            return Task.CompletedTask;
        });
    }

    private static async Task StreamRecoveryAsync(bool interrupted)
    {
        var calls=0;var reserved=0;
        using var http=new HttpClient(new FakeHandler(request=>{
            calls++;
            using var payload=JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Require(payload.RootElement.GetProperty("stream").GetBoolean());
            Require(payload.RootElement.GetProperty("reasoning").GetProperty("effort").GetString()==(calls==1?"high":"low"));
            var data=calls==1
                ? interrupted ? """{"type":"response.created"}""" :
                    """{"type":"response.incomplete","response":{"status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"usage":{"input_tokens":12,"output_tokens":24576}}}"""
                : """{"type":"response.completed","response":{"status":"completed","output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"{\"brand\":\"BMW\",\"series\":\"3 Series\",\"variant\":\"330i\"}"}]}]}}""";
            return new(HttpStatusCode.OK){Content=new StringContent("data: "+data+"\n\n",System.Text.Encoding.UTF8,"text/event-stream")};
        }));
        var images=new FakeImages();
        var service=new ProviderRecognitionService(http,images,new GeminiService(http,images,()=>"test",()=>reserved++),_=>"test",()=>reserved++);
        var settings=new AppSettings{Provider=RecognitionProvider.OpenAI,Strength=RecognitionStrength.High};
        settings.ProviderProfiles[RecognitionProvider.OpenAI]=new(){Model="gpt-6.2-sol"};
        try
        {
            var result=await service.RecognizeAsync([new Photo()],settings,false,default);
            Require(!interrupted && calls==2 && reserved==2 && result.Rounds.Count==2 && result.Identity.Variant=="330i");
        }
        catch(IOException) when(interrupted){Require(calls==1 && reserved==1);}
    }

    private static async Task CheckAsync(RecognitionProvider provider,bool online,RecognitionStrength strength,CancellationTokenSource? cancel=null,bool rateLimit=false)
    {
        var gemini=provider==RecognitionProvider.Gemini;var claude=provider==RecognitionProvider.Claude;var openai=provider==RecognitionProvider.OpenAI;
        var model=gemini?"gemini-4-hydrogen":claude?"claude-sonnet-5-5":openai?"gpt-6.2-sol":"deepseek-flash";
        var settings=new AppSettings {Provider=provider,Model=model,Strength=strength,Search=online?SearchPreference.Grounded:SearchPreference.Gemini};
        settings.ProviderProfiles[provider]=new(){Model=model,Search=online};
        var plan=RecognitionPlan.Create(settings);
        var calls=0;var reservations=0;
        using var http=new HttpClient(new FakeHandler(message=>{
            var index=calls++;
            using var doc=JsonDocument.Parse(message.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var root=doc.RootElement;
            Require(root.TryGetProperty("tools",out var tools)==online);
            Require(root.TryGetProperty("max_tool_calls",out var toolCalls)==(online&&openai));
            if(online&&openai)Require(toolCalls.GetInt32()==plan.SearchTargets[index]);
            if(online&&claude)Require(tools[0].GetProperty("max_uses").GetInt32()==plan.SearchTargets[index]);
            var effort=root.GetProperty(gemini?"generation_config":claude?"output_config":"reasoning").GetProperty(gemini?"thinking_level":"effort").GetString();
            Require(effort==plan.Effort && (!openai || effort!="none"));
            Require(root.GetRawText().Contains("productionYears"));
            Require(root.GetRawText().Contains("adjudicate")== (index==2));
            if(rateLimit && index==1)return new(HttpStatusCode.TooManyRequests);
            var variant=strength==RecognitionStrength.Xhigh && index==0?"3 Series":index==1?"M340i":"330i";
            if(strength==RecognitionStrength.Xhigh && index==1)variant="330i";
            var identity=JsonSerializer.Serialize(new {brand="BMW",series="3 Series",variant,generation="7th",phase=index==1?"Facelift":"Pre-Facelift"});
            var response=gemini?JsonSerializer.Serialize(new {status="completed",output_text=identity,usage=new {total_input_tokens=3,total_output_tokens=4}}):
                claude?JsonSerializer.Serialize(new {stop_reason="end_turn",content=new[]{new {type="text",text=identity}}}):
                JsonSerializer.Serialize(new {status="completed",output=new[]{new {type="message",role="assistant",content=new[]{new {type="output_text",text=identity}}}}});
            if(index==0)cancel?.Cancel();
            return new(HttpStatusCode.OK){Content=new StringContent(response)};
        }));
        var images=new FakeImages();
        var service=new ProviderRecognitionService(http,images,new GeminiService(http,images,()=>"fixture",()=>reservations++),_=>"fixture",()=>reservations++);
        var result=await service.RecognizeAsync([new Photo()],settings,false,cancel?.Token??default);
        Require(calls==(rateLimit?2:plan.SearchTargets.Length) && reservations==calls);
        Require(result.Rounds.Count==(rateLimit?1:calls));
        if(!online)Require(result.SearchCount==0);
        if(rateLimit)Require(result.RoundNotice.Contains("未完成") && result.Identity.Variant=="330i");
        else if(strength==RecognitionStrength.Max)Require(result.Identity.Variant=="" && result.Identity.Phase=="");
        else if(strength==RecognitionStrength.Ultra)Require(result.Identity.Variant=="330i" && result.Identity.Phase=="Pre-Facelift");
    }

    private static void Require(bool value)
    {
        if(!value)throw new InvalidOperationException("Unexpected recognition plan or result.");
    }
}
