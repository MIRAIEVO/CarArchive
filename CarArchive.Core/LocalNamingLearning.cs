using System.Text.Json;
using System.Text.RegularExpressions;

namespace CarArchive.Core;

public sealed class NamingLearningSample
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public CarIdentity Identity { get; set; } = new();
    public string GeneratedName { get; set; } = "";
    public string FinalName { get; set; } = "";
    public string AppliedRule { get; set; } = "";
    public DateTimeOffset ConfirmedAt { get; set; } = DateTimeOffset.Now;
}
public sealed class LocalNamingRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    // Exact, Vocabulary or Suffix. Literal chassis/trim numbers never become a reusable pattern.
    public string Kind { get; set; } = "Vocabulary";
    public string Brand { get; set; } = "";
    public string Family { get; set; } = "";
    public string IdentityKey { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public bool Approved { get; set; }
    public bool Enabled { get; set; } = true;
    public bool SourceBacked { get; set; }
    public bool ManuallyEdited { get; set; }
    public string Conflict { get; set; } = "";
    public List<string> SampleIds { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string Description => $"{(Approved ? "已批准" : "待确认")} · {(Enabled ? "启用" : "停用")} · {Brand} / {(Kind=="Exact"?"精确车型":Family.Length>0?Family:"品牌")} · {Kind}: {From} → {To}"+(Conflict.Length>0?" · "+Conflict:"");
}

public static class LocalNamingLearning
{
    private static readonly string[] Vocabulary = ["Station Wagon","Shooting Brake","Sportback","Hatchback","Cabriolet","Convertible","Coupe","Estate","Wagon","Sedan","Saloon","Facelift","LCI","AMG-Line","AMG Line","S-line","S line"];
    private static readonly string[] Styles = ["{generation} {phase}","{code} {phase}","{years} {phase}","{roman} {phase}","{generation}, {code} {phase}"];
    private static bool EquivalentWords(string from,string to)
    {
        string[][] groups=[["Station Wagon","Wagon","Estate"],["Cabriolet","Convertible"],["Sedan","Saloon"],["AMG-Line","AMG Line"],["S-line","S line"],["Facelift","LCI"]];
        return from!=to && groups.Any(group=>group.Contains(from) && group.Contains(to));
    }
    public static string Family(CarIdentity identity)=>NamingText.Family(identity.Brand,identity.Series,NamingText.FullModel(identity));
    public static IReadOnlyList<LocalNamingRule> RecordConfirmed(PhotoGroup group,WorkspaceState state,bool explicitly)
    {
        if(state.Settings.Learning==LearningMode.Manual && !explicitly) return [];
        if(string.IsNullOrWhiteSpace(group.FolderName) || string.IsNullOrWhiteSpace(NamingText.FullModel(group.Confirmed)) || string.IsNullOrWhiteSpace(group.Confirmed.Brand)) return [];
        NamingService.SafeSegment(group.FolderName);
        Reconcile(state);
        var key=group.Confirmed.NamingKey;
        var previous=state.NamingSamples.FirstOrDefault(s=>s.Identity.NamingKey==key);
        if(previous is not null && previous.FinalName==group.FolderName) return state.LocalNamingRules.Where(r=>!r.Approved && r.Enabled && r.SampleIds.Contains(previous.Id)).ToList();
        var sample=new NamingLearningSample { Identity=group.Confirmed.Clone(),GeneratedName=group.LastGeneratedName,FinalName=LatinText.English(group.FolderName),AppliedRule=group.AppliedRule };
        state.NamingSamples.RemoveAll(s=>s.Identity.NamingKey==key);state.NamingSamples.Add(sample);
        var exact=state.LocalNamingRules.FirstOrDefault(r=>r.Kind=="Exact" && r.IdentityKey==key);
        if(sample.GeneratedName!=sample.FinalName)
        {
            exact ??= new() { Kind="Exact",Brand=sample.Identity.Brand,Family=Family(sample.Identity),IdentityKey=key };
            exact.From=sample.GeneratedName;exact.To=sample.FinalName;exact.SampleIds=[sample.Id];exact.Approved=true;exact.Enabled=true;exact.SourceBacked=true;exact.ManuallyEdited=false;
            exact.Conflict=ValidateExact(sample.Identity,exact.To);
            if(!state.LocalNamingRules.Contains(exact)) state.LocalNamingRules.Add(exact);
        }
        else if(exact is not null) exact.Enabled=false;
        var edits=state.NamingSamples.Select(s=>(Sample:s,Edit:Extract(s))).Where(x=>x.Edit is not null).ToList();
        foreach(var brandGroup in edits.GroupBy(x=>NamingText.PolicyBrand(x.Sample.Identity.Brand)))
        foreach(var format in brandGroup.GroupBy(x=>(x.Edit!.Value.Kind,x.Edit.Value.From,x.Edit.Value.To)))
        {
            foreach(var familyGroup in format.GroupBy(x=>NamingText.Key(Family(x.Sample.Identity))))
                if(familyGroup.Key.Length>0 && familyGroup.Select(x=>NamingText.ModelKey(NamingText.FullModel(x.Sample.Identity))).Distinct().Count()>=2)
                    Upsert(format.Key.Kind,format.Key.From,format.Key.To,familyGroup.First().Sample.Identity.Brand,Family(familyGroup.First().Sample.Identity),familyGroup.Select(x=>x.Sample).ToList(),state);
            if(format.Select(x=>NamingText.Key(Family(x.Sample.Identity))).Where(x=>x.Length>0).Distinct().Count()>=3)
                Upsert(format.Key.Kind,format.Key.From,format.Key.To,format.First().Sample.Identity.Brand,"",format.Select(x=>x.Sample).ToList(),state);
        }
        Reconcile(state);
        return state.LocalNamingRules.Where(r=>!r.Approved && r.Enabled).ToList();
    }
    private static void Upsert(string kind,string from,string to,string brand,string family,List<NamingLearningSample> samples,WorkspaceState state)
    {
        var rule=state.LocalNamingRules.FirstOrDefault(r=>r.Kind==kind && NamingText.PolicyBrand(r.Brand)==NamingText.PolicyBrand(brand) && NamingText.Key(r.Family)==NamingText.Key(family) && r.From==from && r.To==to);
        if(rule is null) { rule=new() { Kind=kind,Brand=brand,Family=family,From=from,To=to,SourceBacked=true,Approved=state.Settings.Learning==LearningMode.Automatic };state.LocalNamingRules.Add(rule); }
        rule.SampleIds=samples.Select(s=>s.Id).Distinct().ToList();
    }
    // Recompute from current confirmations: replacing a sample cannot leave a stale vote behind.
    // Explicitly edited rules remain user decisions, not new automatically inferred evidence.
    public static void Reconcile(WorkspaceState state)
    {
        var edits=state.NamingSamples.Select(s=>(Sample:s,Edit:Extract(s))).Where(x=>x.Edit is not null).ToList();
        foreach(var rule in state.LocalNamingRules)
        {
            rule.Conflict="";
            try { ValidateRule(rule); } catch(ArgumentException error) { rule.Conflict=error.Message;continue; }
            if(rule.Kind=="Exact")
            {
                var sample=state.NamingSamples.FirstOrDefault(s=>s.Identity.NamingKey==rule.IdentityKey);
                if(sample is null) { if(rule.SourceBacked) rule.Conflict="来源确认记录已不存在";continue; }
                rule.Conflict=ValidateExact(sample.Identity,rule.To);
                if(rule.SourceBacked && !rule.ManuallyEdited) rule.SampleIds=[sample.Id];
                continue;
            }
            if(!rule.SourceBacked || rule.ManuallyEdited) continue;
            var support=edits.Where(x=>x.Edit!.Value.Kind==rule.Kind && x.Edit.Value.From==rule.From && x.Edit.Value.To==rule.To
                && NamingText.PolicyBrand(x.Sample.Identity.Brand)==NamingText.PolicyBrand(rule.Brand)
                && (rule.Family.Length==0 || NamingText.Key(Family(x.Sample.Identity))==NamingText.Key(rule.Family)))
                .Select(x=>x.Sample).ToList();
            rule.SampleIds=support.Select(s=>s.Id).Distinct().ToList();
            var sufficient=rule.Family.Length>0
                ?support.Select(s=>NamingText.ModelKey(NamingText.FullModel(s.Identity))).Distinct().Count()>=2
                :support.Select(s=>NamingText.Key(Family(s.Identity))).Where(s=>s.Length>0).Distinct().Count()>=3;
            if(!sufficient) rule.Conflict="当前确认样本不足，暂停应用";
        }
        var groups=state.LocalNamingRules.Where(r=>r.Enabled && r.Kind!="Exact" && r.Conflict.Length==0)
            .GroupBy(r=>(Brand:NamingText.PolicyBrand(r.Brand),Family:NamingText.Key(r.Family),r.Kind,From:r.Kind=="Suffix"?"":r.From));
        foreach(var peers in groups)
            if(peers.Select(r=>r.To).Distinct().Count()>1)
                foreach(var rule in peers) rule.Conflict="同一适用范围存在不同确认习惯，请禁用不适用规则";
    }
    private static (string Kind,string From,string To)? Extract(NamingLearningSample sample)
    {
        var before=LatinText.English(NamingText.Clean(sample.GeneratedName));var after=LatinText.English(NamingText.Clean(sample.FinalName));
        if(before.Length==0 || before==after) return null;
        foreach(var from in Vocabulary.OrderByDescending(s=>s.Length))
        foreach(var to in Vocabulary.OrderByDescending(s=>s.Length))
            if(EquivalentWords(from,to) && Regex.Replace(before,@"(?<!\w)"+Regex.Escape(from)+@"(?!\w)",_=>to)==after)
                return ("Vocabulary",from,to);
        var b=Split(before);var a=Split(after);
        if(NamingText.ModelKey(b.Model)!=NamingText.ModelKey(a.Model)) return null;
        var oldStyle=Styles.FirstOrDefault(s=>Render(s,sample.Identity)==b.Suffix);var newStyle=Styles.FirstOrDefault(s=>Render(s,sample.Identity)==a.Suffix);
        return oldStyle is not null && newStyle is not null && oldStyle!=newStyle?("Suffix",oldStyle,newStyle):null;
    }
    private static (string Model,string Suffix) Split(string name)
    {
        var match=Regex.Match(name,@"^(.*?)\s*\(([^()]*)\)$");return match.Success?(match.Groups[1].Value.Trim(),match.Groups[2].Value.Trim()):(name,"");
    }
    public static string Render(string style,CarIdentity identity)
    {
        var generation=NamingText.Generation(identity.Generation);var number=Regex.Match(generation,@"^\d+").Value;
        var values=new Dictionary<string,string> { ["generation"]=generation,["code"]=NamingText.Clean(identity.Code),["years"]=NamingText.Years(identity.ProductionYears),["roman"]=number.Length>0?NamingText.Roman(int.Parse(number)):"",["phase"]=NamingText.Phase(identity.Phase) };
        if(Regex.Matches(style,@"\{([^}]+)\}").Any(m=>!values.ContainsKey(m.Groups[1].Value) || m.Groups[1].Value!="phase" && values[m.Groups[1].Value].Length==0)) return "";
        return NamingText.Clean(Regex.Replace(style,@"\{([^}]+)\}",m=>values[m.Groups[1].Value]));
    }
    public static string ValidateExact(CarIdentity identity,string proposed)
    {
        try { NamingService.SafeSegment(proposed); } catch(ArgumentException e) { return e.Message; }
        var parts=Split(proposed);var full=LatinText.English(NamingText.StripBrand(NamingText.WithoutDrivetrain(NamingText.FullModel(identity),identity.Brand),identity.Brand));
        // Exact aliases may change spelling, but cannot discard a specific trim/package or add new digits.
        var originalDigits=Regex.Matches(full,@"\d+").Select(m=>m.Value).ToArray();var proposedDigits=Regex.Matches(parts.Model,@"\d+").Select(m=>m.Value).ToArray();
        static string CoreWords(string value)
        {
            // Normalize equivalent naming words, but retain their presence: a full body/package name cannot be dropped.
            foreach(var pair in new[]{("Station Wagon","Wagon"),("Estate","Wagon"),("Cabriolet","Convertible"),("Saloon","Sedan"),("AMG-Line","AMG Line"),("S-line","S line")})
                value=Regex.Replace(value,@"(?<!\w)"+Regex.Escape(pair.Item1)+@"(?!\w)",_=>pair.Item2,RegexOptions.IgnoreCase);
            return NamingText.ModelKey(value);
        }
        if(CoreWords(full)!=CoreWords(parts.Model)) return "不得省略或更改完整型号中的配置、套件及特别版名称";
        if(!originalDigits.SequenceEqual(proposedDigits)) return "型号数字发生变化，不能作为命名规则；请先核对识别字段";
        if(NamingText.StripBrand(parts.Model,identity.Brand)!=parts.Model) return "名称不应带品牌前缀";
        if(NamingText.WithoutDrivetrain(parts.Model,identity.Brand)!=parts.Model) return "与四驱标识省略要求冲突";
        if(NamingText.RemoveRepeatedBodyTerms(parts.Model)!=parts.Model) return "存在重复车身词";
        var brand=NamingText.PolicyBrand(identity.Brand);
        if(brand=="bmw" && Regex.IsMatch(parts.Suffix,"Facelift",RegexOptions.IgnoreCase)) return "BMW 改款必须使用 LCI";
        if(brand=="porsche" && Family(identity)=="911")
        { var code=NamingText.Porsche911Code(identity.Code,identity.Phase,out _);if(code.Length>0 && parts.Suffix!=code) return "与 Porsche 911 改款代号要求冲突"; }
        if(brand=="audi" || brand=="rolls royce")
        { var expected=Split(NamingGuideEngine.Generate(identity,new()).Name).Suffix;if(parts.Suffix!=expected) return "与该品牌明确补充要求冲突"; }
        return "";
    }
    public static void ValidateRule(LocalNamingRule rule)
    {
        if(string.IsNullOrWhiteSpace(rule.Brand)) throw new ArgumentException("本地学习规则必须限定品牌。");
        if(rule.Kind=="Suffix" && (!Styles.Contains(rule.From) || !Styles.Contains(rule.To))) throw new ArgumentException("后缀规则只能选用已支持的代次、代号或年份模板。");
        if(rule.Kind=="Vocabulary" && (!Vocabulary.Contains(rule.From) || !Vocabulary.Contains(rule.To) || !EquivalentWords(rule.From,rule.To))) throw new ArgumentException("用词规则只能替换同义命名词，不可改变车身类型、套件或型号数字。");
        if(rule.Kind is not ("Exact" or "Suffix" or "Vocabulary")) throw new ArgumentException("未知规则类型。");
        if(rule.Kind=="Exact") NamingService.SafeSegment(rule.To);
    }
    public static string SuffixStyle(CarIdentity identity,WorkspaceState state,string fallback,out string applied)
    {
        applied="";var rule=Eligible(identity,state).FirstOrDefault(r=>r.Kind=="Suffix" && Render(r.To,identity).Length>0);
        if(rule is null) return fallback;applied="本地学习 "+rule.Id+" · "+rule.Description;return rule.To;
    }
    public static string VocabularyModel(CarIdentity identity,WorkspaceState state,string model,out string applied)
    {
        var reasons=new List<string>();
        foreach(var rule in Eligible(identity,state).Where(r=>r.Kind=="Vocabulary").GroupBy(r=>r.From).Select(g=>g.First()))
        { try { ValidateRule(rule); } catch(ArgumentException) { continue; }var updated=Regex.Replace(model,@"(?<!\w)"+Regex.Escape(rule.From)+@"(?!\w)",_=>rule.To,RegexOptions.IgnoreCase);if(updated!=model) reasons.Add(rule.Id);model=updated; }
        applied=reasons.Count>0?"本地用词 "+string.Join(",",reasons):"";return model;
    }
    public static NamingDecision ApplyExact(CarIdentity identity,WorkspaceState state,NamingDecision generated)
    {
        var rule=Eligible(identity,state).FirstOrDefault(r=>r.Kind=="Exact" && r.IdentityKey==identity.NamingKey);
        if(rule is null || ValidateExact(identity,rule.To).Length>0) return generated;
        return new(LatinText.English(rule.To),"本地精确车型 "+rule.Id+" · "+rule.Description,generated.Warnings);
    }
    private static IEnumerable<LocalNamingRule> Eligible(CarIdentity identity,WorkspaceState state)=>state.LocalNamingRules.Where(r=>r.Enabled && r.Approved && r.Conflict.Length==0 && NamingText.PolicyBrand(r.Brand)==NamingText.PolicyBrand(identity.Brand) && (r.Family.Length==0 || NamingText.Key(r.Family)==NamingText.Key(Family(identity))))
        .OrderByDescending(r=>r.Kind=="Exact").ThenByDescending(r=>r.Family.Length>0).ThenByDescending(r=>r.CreatedAt);
    public static string Export(WorkspaceState state)=>JsonSerializer.Serialize(new { Version=1,state.NamingSamples,state.LocalNamingRules },new JsonSerializerOptions(StateStore.Json) { WriteIndented=true });
}
