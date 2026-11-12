using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CarArchive.Core;

public static class NamingGuideStore
{
    public static string DirectoryFor(string dataDirectory,string root)
    {
        var canonical=root.Length==0?"":Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        return Path.Combine(dataDirectory,"naming",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..12]);
    }
    public static NamingGuide? Load(string directory,string root)
    {
        var file=Path.Combine(directory,"naming-guide.json");if(!File.Exists(file)) return null;
        var guide=JsonSerializer.Deserialize<NamingGuide>(File.ReadAllText(file),StateStore.Json) ?? throw new InvalidDataException("本地命名指南为空。");
        if(guide.Version!=NamingGuide.CurrentVersion || !Path.GetFullPath(guide.SourceRoot).Equals(Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("命名指南版本或归档根目录不匹配。");
        if(guide.Profiles.Any(p=>System.Text.RegularExpressions.Regex.Matches(p.Style,@"\{([^}]+)\}").Any(m=>m.Groups[1].Value is not ("generation" or "phase" or "code" or "number" or "roman" or "years"))))
            throw new InvalidDataException("命名指南含不支持的占位符。");
        return guide;
    }
    public static bool IsFresh(NamingGuide guide)
    {
        if(guide.SourceStateHash.Length==0) return false;
        var paths=Directory.EnumerateDirectories(guide.SourceRoot).SelectMany(Directory.EnumerateDirectories).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(!paths.SetEquals(guide.Inventory.Select(entry=>entry.Path))) return false;
        var notes=Directory.EnumerateDirectories(guide.SourceRoot).Append(guide.SourceRoot).SelectMany(path=>Directory.EnumerateFiles(path,"*.txt"))
            .Where(path=>Path.GetFileName(path).Contains("补充命名",StringComparison.Ordinal)).ToList();
        return notes.Count==guide.Notes.Count && guide.Notes.All(note=>File.Exists(note.Path) && note.Hash==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(note.Path))));
    }
    public static bool IsFresh(NamingGuide guide,WorkspaceState state,IReadOnlyList<ArchiveJob>? jobs=null)
        => guide.SourceStateHash.Length>0 && guide.SourceStateHash==SourceStateHash(state,jobs) && IsFresh(guide);
    public static string SourceStateHash(WorkspaceState state,IReadOnlyList<ArchiveJob>? jobs=null)
    {
        var archiveDates=(jobs ?? []).Where(job=>job.Status=="Completed" && job.Direction=="Archive")
            .GroupBy(job=>job.GroupId).ToDictionary(group=>group.Key,group=>group.Max(job=>job.CreatedAt));
        var source=new
        {
            Names=state.Names.Select(name=>new { name.IdentityKey,name.Brand,name.FolderName }).ToArray(),
            BrandFolders=state.BrandFolders.OrderBy(pair=>pair.Key,StringComparer.OrdinalIgnoreCase).ToArray(),
            Groups=state.Groups.Where(group=>group.Archived).Select(group=>new
            {
                group.Id,group.TargetBrand,group.FolderName,Identity=group.Confirmed.NamingKey,
                ArchivedAt=archiveDates.GetValueOrDefault(group.Id)
            }).ToArray()
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(source,StateStore.Json)));
    }
    public static void Save(string directory,NamingGuide guide,WorkspaceState state)
    {
        Directory.CreateDirectory(directory);
        List<string[]> ruleRows = [
            ["规则编号","品牌目录","车系","具体型号匹配","代次条件","后缀模板","名称前缀","使用独立套件","样本票数","冲突处理","型号别名","来源目录","补充要求与说明","改款写法","来源样本新命名示例"],
            ["default","全部未收录车系","*","*","*","{generation} {phase}","去品牌，从车系／型号开始","已确认时去重补齐","0","代次缺失时留空提示","","","完整营销名称／完整型号；保留车型、套件、特别版；Body仅展示；Pre-Facelift省略","Facelift / Late","Model Sport Coupe Anniversary Edition (3rd Facelift)"],
            ["Audi-policy","Audi","A4/A5/A6/A7/A8及衍生","*","*","{code} {phase}","","按车型规则","","补充要求优先","","","B/C/D常用代号；其他Audi用基础车系代次；去驱动quattro；RS3/A8L格式","Facelift / Late","A4 Avant (B9 Facelift)"],
            ["BMW-policy","BMW","*","*","*","按车型规则","","已确认时去重补齐","","补充要求优先","","","Facelift替换为LCI；省略xDrive（含连接型号的形式）","LCI / Late","M3 Competition Sedan (G80 LCI)"],
            ["Rolls-policy","Rolls-Royce","Phantom / 其他现代 / 其他老车","*","*","{roman} / {generation} / {years}","","已确认时去重补齐","","用户明确要求","","","Phantom保留VII/VIII；其他现代用现代代次；其他老车用生产年份范围","Facelift / Late","Phantom (VIII)"],
            ["Mercedes-policy","Mercedes-Benz / Mercedes-AMG / Mercedes-Maybach","*","*","*","按车型规则","去品牌，不恢复旧Benz前缀","已确认时去重补齐","","当前实际品牌目录优先于已不存在的映射","","","省略4MATIC/4MATIC+；旧名称仅作为后缀依据，不缩短完整型号","Facelift / Late","S 580 (W223)"],
            ["Porsche911-policy","Porsche","911","*","*","{code}","","已确认时去重补齐","","用户最新要求优先于历史格式","","","996/997/991/992：中改前基础代号，不写.1；中改后.2，不另加Facelift；早期其他代号保留，不推测.2","以.2表示中改","911 Carrera GTS Cabriolet (992.2)"]
        ];
        foreach(var p in guide.Profiles)
        {
            var entries=p.Sources.Where(guide.Index.Paths.ContainsKey).Select(path=>guide.Index.Paths[path]).ToList();
            var example=entries[0];var exampleName=NamingGuideEngine.Generate(new() { Brand=example.Brand,Series=example.Family,Variant=example.Model,Generation=example.Generation,Code=example.RawCode,Phase=example.Phase,Package=example.Package,ProductionYears=example.ProductionYears },state,guide).Name;
            ruleRows.Add([p.Id,p.Brand,p.Family,p.Model,p.Generation,p.Style,"完整名称去品牌","已确认时去重补齐",p.Votes.ToString(),p.Resolution,
                string.Join(" | ",entries.SelectMany(e=>e.Aliases).Distinct(StringComparer.OrdinalIgnoreCase)),string.Join(" | ",p.Sources),
                string.Join(" | ",entries.Select(e=>e.Warning).Where(x=>x.Length>0).Distinct()),p.Brand.Equals("Porsche",StringComparison.OrdinalIgnoreCase) && p.Family=="911"?"现代改款使用.2":p.PhaseWord+" / Late",exampleName]);
        }
        List<string[]> inventory = [ ["品牌目录","原始文件夹名","完整路径","车系","规范型号别名","原始后缀","原始代次","原始代号","指南代号","改款","有确认字段","警告","新规则示例","采用规则"] ];
        foreach(var e in guide.Inventory)
        {
            var sample=new CarIdentity { Brand=e.Brand,Series=e.Family,Variant=e.Model,Generation=e.Generation,Code=e.Code.Length>0?e.Code:e.RawCode,Phase=e.Phase,Package=e.Package,ProductionYears=e.ProductionYears };
            var decision=NamingGuideEngine.Generate(sample,state,guide);
            inventory.Add([e.Brand,e.Folder,e.Path,e.Family,string.Join(" | ",e.Aliases),e.Suffix,e.Generation,e.RawCode,e.Code,e.Phase,e.HasIdentity?"是":"否",e.Warning,decision.Name,decision.Reason]);
        }
        var notes=new StringBuilder("# 本地命名指南\n\n指南仅在本机运行，不上传至 Gemini。现有目录不自动重命名。\n\n");
        notes.AppendLine($"扫描目录：{guide.SourceRoot}\n分类目录：{guide.Inventory.Count}\n规则：{guide.Profiles.Count}\n生成时间：{guide.CreatedAt:O}\n");
        notes.AppendLine("统一：完整营销名称或完整型号（去品牌，保留已确认车型、套件、特别版，不拼接 Body）＋括号后缀。历史简称和品牌前缀不替换完整名称。\n括号按品牌／车系／型号指南选择代次、代号或年代；未收录车系默认代次，缺失不猜测。套件已确认且完整名称未包含时去重补齐。\n奔驰（含AMG/Maybach）、宝马、奥迪去驱动标记4MATIC、xDrive、quattro；保留Sport Quattro固有车名。\n911：996/997/991/992中改前为基础代号，中改后为.2，不再另加Facelift；其他早期代号保留，不强行追加.2。\n补充要求优先于历史格式；Phantom 使用 VII/VIII，其他现代劳斯莱斯用现代代次，其他老车用年份范围。\n命名指南.csv 为规则表；命名扫描清单.csv 覆盖每个实际目录；naming-guide.json 为运行数据（可备份编辑，重新扫描会重新生成）。\n");
        foreach(var note in guide.Notes) notes.AppendLine($"## {note.Path}\n\n{note.Text}\n");
        foreach(var warning in guide.Warnings) notes.AppendLine("注意："+warning);
        WriteAtomic(Path.Combine(directory,"命名指南.csv"),Csv(ruleRows),true);
        WriteAtomic(Path.Combine(directory,"命名扫描清单.csv"),Csv(inventory),true);
        WriteAtomic(Path.Combine(directory,"说明与补充要求.md"),notes.ToString(),true);
        // Publish the machine-readable guide last; a reader never sees a partial JSON file.
        WriteAtomic(Path.Combine(directory,"naming-guide.json"),JsonSerializer.Serialize(guide,new JsonSerializerOptions(StateStore.Json) { WriteIndented=true }),false);
    }
    public static void WriteAtomic(string path,string text,bool bom)
    {
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllText(temp,text,new UTF8Encoding(bom));File.Move(temp,path,true); }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }
    private static string Csv(IEnumerable<string[]> rows) => string.Join("\r\n",rows.Select(row=>string.Join(",",row.Select(value=>"\""+(value.Length>0 && "=+-@".Contains(value[0])?"'":"")+value.Replace("\"","\"\"")+"\""))))+"\r\n";
}
