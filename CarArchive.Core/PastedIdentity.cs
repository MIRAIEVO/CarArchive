using System.Text.Json;
using System.Text.RegularExpressions;
namespace CarArchive.Core;
public static class PastedIdentity
{
 public static CarIdentity Parse(string text)
 {
  if(text.Length>128*1024)throw new InvalidDataException("粘贴内容过长，请只保留车型 JSON。");
  var fence=Regex.Match(text,@"```(?:json)?\s*([\s\S]*?)```",RegexOptions.IgnoreCase);if(fence.Success)text=fence.Groups[1].Value;
  var start=text.IndexOf('{');if(start<0)throw new InvalidDataException("未找到 JSON 对象。");
  int depth=0,end=-1;bool quoted=false,escaped=false;
  for(var i=start;i<text.Length;i++){var c=text[i];if(quoted){if(escaped)escaped=false;else if(c=='\\')escaped=true;else if(c=='"')quoted=false;continue;}if(c=='"'){quoted=true;continue;}if(c=='{')depth++;if(c=='}'&&--depth==0){end=i+1;break;}}
  if(end<0)throw new InvalidDataException("JSON 不完整，请检查结束大括号。");
  using var doc=JsonDocument.Parse(text[start..end],new JsonDocumentOptions{AllowTrailingCommas=true,CommentHandling=JsonCommentHandling.Skip});var root=doc.RootElement;
  foreach(var property in root.EnumerateObject())if(property.Value.ValueKind==JsonValueKind.Object && property.Name is "Identity" or "identity" or "correctedIdentity" or "CorrectedIdentity"){root=property.Value;break;}
  string Read(string name){var values=root.EnumerateObject().Where(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).ToArray();if(values.Length>1)throw new InvalidDataException("字段重复："+name);if(values.Length==0)return "";var value=values[0].Value;var result=value.ValueKind switch{JsonValueKind.String=>value.GetString()??"",JsonValueKind.Null=>"",JsonValueKind.Number=>value.GetRawText(),_=>throw new InvalidDataException("字段必须是文字："+name)};if(result.Length>512)throw new InvalidDataException("字段过长："+name);return result.Trim();}
  var identity=new CarIdentity{Brand=Read("Brand"),Series=Read("Series"),Variant=Read("Variant"),Body=Read("Body"),OfficialName=Read("OfficialName"),Generation=Read("Generation"),Code=Read("Code"),Phase=Read("Phase"),Package=Read("Package"),ProductionYears=Read("ProductionYears")};
  if(identity.Brand.Length==0 || new[]{identity.Series,identity.Variant,identity.OfficialName}.All(string.IsNullOrWhiteSpace))throw new InvalidDataException("至少需要 Brand，以及 Series、Variant 或 OfficialName 中的一项。");return identity;
 }
}
