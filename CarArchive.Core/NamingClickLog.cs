using System.Globalization;
using System.Text;
using System.Text.Json;
namespace CarArchive.Core;
public sealed class NamingClickLog
{
 public DateTimeOffset At {get;set;}=DateTimeOffset.Now;
 public string GroupId {get;set;}="";
 public CarIdentity Original {get;set;}=new();
 public CarIdentity Confirmed {get;set;}=new();
 public string AutomaticName {get;set;}="";
 public string FinalName {get;set;}="";
 public string Rule {get;set;}="";
}
public sealed partial class StateStore
{
 public void RecordNamingClick(NamingClickLog entry)
 {
  var json=JsonSerializer.Serialize(entry,Json);lock(gate){using var db=Open();using var tx=db.BeginTransaction();using var c=db.CreateCommand();c.Transaction=tx;c.CommandText="CREATE TABLE IF NOT EXISTS naming_click_log(id INTEGER PRIMARY KEY AUTOINCREMENT,json TEXT NOT NULL); INSERT INTO naming_click_log(json) VALUES($json); DELETE FROM naming_click_log WHERE id NOT IN (SELECT id FROM naming_click_log ORDER BY id DESC LIMIT 1000)";c.Parameters.AddWithValue("$json",json);c.ExecuteNonQuery();tx.Commit();}
 }
 public List<NamingClickLog> NamingClicks()
 {
  lock(gate){using var db=Open();using var c=db.CreateCommand();c.CommandText="SELECT name FROM sqlite_master WHERE type='table' AND name='naming_click_log'";if(c.ExecuteScalar() is null)return [];c.CommandText="SELECT json FROM naming_click_log ORDER BY id";using var reader=c.ExecuteReader();var rows=new List<NamingClickLog>();while(reader.Read())rows.Add(JsonSerializer.Deserialize<NamingClickLog>(reader.GetString(0),Json)!);return rows;}
 }
 public void ClearNamingClicks()
 {
  lock(gate){using var db=Open();using var c=db.CreateCommand();c.CommandText="DROP TABLE IF EXISTS naming_click_log";c.ExecuteNonQuery();}
 }
 public string ExportNamingClicksCsv()
 {
  var fields=typeof(CarIdentity).GetProperties().Where(p=>p.CanWrite&&p.PropertyType==typeof(string)).ToArray();var text=new StringBuilder();
  string Cell(string value){if(value.TrimStart().StartsWith('=')||value.TrimStart().StartsWith('+')||value.TrimStart().StartsWith('-')||value.TrimStart().StartsWith('@'))value="'"+value;return "\""+value.Replace("\"","\"\"")+"\"";}
  void Row(IEnumerable<string> values)=>text.AppendLine(string.Join(",",values.Select(Cell)));
  Row(new[]{"时间","分组ID"}.Concat(fields.Select(p=>"原始_"+p.Name)).Concat(fields.Select(p=>"确认_"+p.Name)).Concat(new[]{"自动生成名称","手动最终名称","采用规则"}));
  foreach(var row in NamingClicks())Row(new[]{row.At.ToString("O",CultureInfo.InvariantCulture),row.GroupId}.Concat(fields.Select(p=>(string?)p.GetValue(row.Original)??"")).Concat(fields.Select(p=>(string?)p.GetValue(row.Confirmed)??"")).Concat(new[]{row.AutomaticName,row.FinalName,row.Rule}));return text.ToString();
 }
}
