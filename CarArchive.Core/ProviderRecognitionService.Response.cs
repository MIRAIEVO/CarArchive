using System.Text.Json;

namespace CarArchive.Core;

public sealed partial class ProviderRecognitionService
{
    private sealed class OpenAiEmptyOutputException(string message,JsonElement root) : IOException(message)
    {
        public string Usage { get; } = Get(root,"usage") is {ValueKind:JsonValueKind.Object} usage?usage.GetRawText():"";
        public int? Thinking { get; } = Number(Get(Get(root,"usage"),"output_tokens_details"),"reasoning_tokens");
    }
    private static JsonElement Get(JsonElement node,string key) =>
        node.ValueKind==JsonValueKind.Object && node.TryGetProperty(key,out var value) ? value : default;
    private static string Str(JsonElement node,string key) =>
        Get(node,key) is {ValueKind:JsonValueKind.String} value ? value.GetString() ?? "" : "";
    private static int? Number(JsonElement node,string key) =>
        Get(node,key) is {ValueKind:JsonValueKind.Number} value && value.TryGetInt32(out var number) ? number : null;
    private static IEnumerable<JsonElement> Items(JsonElement node,string key) =>
        Get(node,key) is {ValueKind:JsonValueKind.Array} value ? value.EnumerateArray().ToArray() : [];

    private static RecognitionResult Parse(JsonElement root,RecognitionProvider provider,ProviderProfile profile)
    {
        var claude=provider==RecognitionProvider.Claude;
        var status=Str(root,claude?"stop_reason":"status");
        if (status=="incomplete" && provider==RecognitionProvider.OpenAI && Str(Get(root,"incomplete_details"),"reason")=="max_output_tokens")
            throw new OpenAiEmptyOutputException("OpenAI 已用完本轮输出预算（含思考 Token）。",root);
        if (status is "incomplete" or "max_tokens") throw new InvalidDataException("模型输出被截断，未采用结果。");
        if (status!=(claude?"end_turn":"completed")) throw new InvalidDataException($"{provider} 未完成识别或未返回最终文本。");
        var output=Items(root,"output").ToArray();
        var blocks=(claude?Items(root,"content"):output.Where(item=>Str(item,"type")=="message" && Str(item,"role")=="assistant")
            .SelectMany(item=>Items(item,"content"))).Where(block=>Str(block,"type")== (claude?"text":"output_text")).ToArray();
        var raw=string.Join("\n",blocks.Select(block=>Str(block,"text"))).Trim();
        if(provider==RecognitionProvider.OpenAI && raw.Length==0 &&
            !output.SelectMany(item=>Items(item,"content")).Any(item=>Str(item,"type")=="refusal"))
            throw new OpenAiEmptyOutputException("OpenAI 本轮未返回最终文本。",root);
        if (raw.StartsWith("```") && raw.EndsWith("```") && raw.IndexOf('\n') is var newline && newline>=0) raw=raw[(newline+1)..^3].Trim();
        RecognitionResult result;
        try
        {
            using var json=JsonDocument.Parse(raw);
            var identity=Get(json.RootElement,"identity");
            if (identity.ValueKind==JsonValueKind.Undefined) identity=json.RootElement;
            if (Get(identity,"brand").ValueKind!=JsonValueKind.String || Get(identity,"series").ValueKind!=JsonValueKind.String)
                throw new JsonException();
            result=GeminiService.ParseRecognition(JsonSerializer.SerializeToElement(new {output_text=raw}),$"{provider} · {profile.Model}");
        }
        catch (JsonException) {throw new InvalidDataException("模型未返回可用的车型 JSON，未采用结果。");}
        if (result.Identity.Brand.Length==0 || (result.Identity.Series.Length==0 && result.Identity.Variant.Length==0 && result.Identity.OfficialName.Length==0))
            throw new InvalidDataException("图片中的品牌或车型无法确定，未覆盖原有字段。");

        var usage=Get(root,"usage");
        if (usage.ValueKind==JsonValueKind.Object) result.RawUsage=usage.GetRawText();
        result.InputTokens=Number(usage,"input_tokens")??0;
        result.OutputTokens=Number(usage,"output_tokens")??0;
        result.ThinkingTokens=Number(Get(usage,"output_tokens_details"),"reasoning_tokens");
        if (!profile.Search)
        {
            result.SearchCount=0;result.ActualSearch="未启用联网搜索";return result;
        }
        var actions=output.Where(item=>Str(item,"type")=="web_search_call" && Str(item,"status")=="completed")
            .Select(item=>Get(item,"action")).Where(action=>Str(action,"type")=="search").ToArray();
        result.Queries=actions.SelectMany(action=>Items(action,"queries").Where(value=>value.ValueKind==JsonValueKind.String)
            .Select(value=>value.GetString() ?? "").Append(Str(action,"query"))).Where(value=>value.Length>0).Distinct().ToList();
        var citations=blocks.SelectMany(block=>Items(block,claude?"citations":"annotations"))
            .Where(node=>Str(node,"type")== (claude?"web_search_result_location":"url_citation"))
            .Concat(actions.SelectMany(action=>Items(action,"sources")));
        var seen=new HashSet<string>();
        foreach(var citation in citations)
        {
            var url=Str(citation,"url");
            if (!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme is not ("https" or "http") || !seen.Add(url)) continue;
            var title=Str(citation,"title");
            result.Sources.Add(new(title.Length>0?title:uri.Host,url,Str(citation,claude?"cited_text":"snippet")));
        }
        var tools=Get(usage,"server_tool_use");
        var deepCount=Number(tools,"tool_search");
        var imageCount=Number(tools,"tool_image_search");
        result.SearchCount=provider switch
        {
            RecognitionProvider.Claude=>Number(tools,"web_search_requests"),
            RecognitionProvider.DeepSeek when deepCount is not null || imageCount is not null => (deepCount??0)+(imageCount??0),
            _ when actions.Length>0=>actions.Sum(action=>Math.Max(1,Items(action,"queries").Count())),
            _=>null
        };
        result.ActualSearch=result.Sources.Count>0?$"{provider} 联网搜索":result.SearchCount>0?"已搜索，未返回来源":"已启用，未返回搜索记录";
        return result;
    }
}
