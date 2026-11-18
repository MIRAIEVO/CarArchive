using System.Net;
using System.Text;
using System.Text.Json;
using CarArchive.Core;

static class ProviderTests
{
    public static void Register(Action<string,Func<Task>> test)
    {
        foreach(var provider in new[]{RecognitionProvider.OpenAI,RecognitionProvider.Claude,RecognitionProvider.DeepSeek})
            foreach(var search in new[]{false,true})
                test($"{provider}: image, prompt, search={search} and usage",()=>ExerciseAsync(provider,search));
        test("Provider errors do not echo credentials or silently retry",()=>ExerciseAsync(RecognitionProvider.OpenAI,false,true));
    }

    private static async Task ExerciseAsync(RecognitionProvider provider,bool search,bool error=false)
    {
        var claude=provider==RecognitionProvider.Claude;
        var deep=provider==RecognitionProvider.DeepSeek;
        var calls=0;
        var attempts=0;
        using var http=new HttpClient(new FakeHandler(request=>{
            attempts++;
            var uri=request.RequestUri!;
            Require(uri.Host==(claude?"api.anthropic.com":deep?"api.deepseek.com":"api.openai.com"));
            Require(uri.AbsolutePath==(claude?"/v1/messages":deep?"/responses":"/v1/responses"));
            Require(claude?request.Headers.GetValues("x-api-key").Single()=="fixture-key":request.Headers.Authorization?.Parameter=="fixture-key");
            using var doc=JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var payload=doc.RootElement;
            var content=payload.GetProperty(claude?"messages":"input")[0].GetProperty("content");
            Require(content.GetArrayLength()==5);
            Require(content[0].GetProperty("text").GetString()!.Contains("generation"));
            Require(payload.GetProperty(claude?"system":"instructions").GetString()!.Contains("fixture prompt"));
            Require(claude?content[2].GetProperty("source").GetProperty("data").GetString()=="AQID":content[2].GetProperty("image_url").GetString()=="data:image/jpeg;base64,AQID");
            Require(payload.TryGetProperty("tools",out var tools)==search);
            if(search) Require(tools[0].GetProperty("type").GetString()==(claude?"web_search_20250305":deep?"deepseek_web_search":"web_search"));
            if(error) return new(HttpStatusCode.TooManyRequests){Content=new StringContent("fixture-key")};
            const string identity="""{"brand":"BMW","series":"3 Series","generation":"7th"}""";
            var citation=new {type=claude?"web_search_result_location":"url_citation",url="https://example.org/car",title="Reference"};
            var answer=claude
                ? JsonSerializer.Serialize(new {stop_reason="end_turn",content=new[]{new{type="text",text=identity,citations=new[]{citation}}},usage=new{input_tokens=120,output_tokens=30,server_tool_use=new{web_search_requests=1}}})
                : JsonSerializer.Serialize(new {status="completed",output=new[]{new{type="message",role="assistant",content=new[]{new{type="output_text",text=identity,annotations=new[]{citation}}}}},usage=new{input_tokens=120,output_tokens=30,output_tokens_details=new{reasoning_tokens=5}}});
            return new(HttpStatusCode.OK){Content=new StringContent(answer,Encoding.UTF8,"application/json")};
        }));
        var images=new FakeImages();
        var service=new ProviderRecognitionService(http,images,new GeminiService(http,images,()=>throw new Exception("Unexpected Gemini key lookup"),()=>calls++),
            selected=>selected==provider?"fixture-key":throw new Exception("Incorrect key selection"),()=>calls++);
        var settings=new AppSettings{Provider=provider,ExplicitUploadSelection=true};
        settings.ProviderProfiles[provider]=new(){Model=ProviderCatalog.Models(provider)[0],Prompt="fixture prompt",Search=search};
        try
        {
            var result=await service.RecognizeAsync([new Photo(),new Photo()],settings,false,default);
            Require(!error && result.Identity.Brand=="BMW" && result.InputTokens==120 && result.OutputTokens==30);
            Require(result.Sources.Count==(search?1:0));
            if(!search)Require(result.SearchCount==0);
        }
        catch(IOException exception) when(error)
        {
            Require(exception.Message.Contains("429") && !exception.Message.Contains("fixture-key"));
        }
        Require(calls==1 && attempts==1);
    }

    private static void Require(bool condition)
    {
        if(!condition) throw new InvalidOperationException("Provider request/response contract did not match.");
    }
}
