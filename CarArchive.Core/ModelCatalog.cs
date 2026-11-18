using System.Net.Http;
using System.Text.Json;
namespace CarArchive.Core;
public static class ModelCatalog
{
 // Official model catalog verified 2026-11-16: https://aistudio.google.com/docs/models
 public static readonly string[] BuiltIn=["gemini-3.8-flash","gemini-4-hydrogen","gemini-4-argon","gemini-4-carbon","gemini-3.7-flash","gemini-3.6-flash","gemini-3.5-flash","gemini-3.5-flash-lite","gemini-3.1-pro-preview","gemini-3.1-flash-lite","gemini-3-flash-preview","gemini-3-pro-preview"];
 public static async Task<IReadOnlyList<string>> FetchAsync(HttpClient http,string key,CancellationToken ct)
 {
  if(string.IsNullOrWhiteSpace(key))throw new InvalidOperationException("请先填写 API Key。");var models=new HashSet<string>(StringComparer.OrdinalIgnoreCase);string? page=null;
  for(var i=0;i<5;i++) {
   using var request=new HttpRequestMessage(HttpMethod.Get,"https://generativelanguage.googleapis.com/v1beta/models?pageSize=100"+(page is null?"":"&pageToken="+Uri.EscapeDataString(page)));request.Headers.Add("x-goog-api-key",key.Trim());
   using var response=await http.SendAsync(request,ct);if(!response.IsSuccessStatusCode)throw new IOException("模型列表读取失败：HTTP "+(int)response.StatusCode+"。请检查 Key、网络与账号权限。");
   using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));if(!doc.RootElement.TryGetProperty("models",out var list))throw new InvalidDataException("接口没有返回模型清单。");
   foreach(var model in list.EnumerateArray())if(model.TryGetProperty("name",out var name)) {if(model.TryGetProperty("supportedGenerationMethods",out var methods)&&!methods.EnumerateArray().Any(m=>m.GetString()=="generateContent"))continue;var id=(name.GetString()??"").Replace("models/","");if(id.StartsWith("gemini-",StringComparison.OrdinalIgnoreCase)&&!new[]{"image","tts","live","audio","robotics","embedding"}.Any(w=>id.Contains(w,StringComparison.OrdinalIgnoreCase)))models.Add(id);}
   page=doc.RootElement.TryGetProperty("nextPageToken",out var token)?token.GetString():null;if(string.IsNullOrEmpty(page))break;
  }return models.Order(StringComparer.OrdinalIgnoreCase).ToArray();
 }
}
