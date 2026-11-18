using System.Text.RegularExpressions;

namespace CarArchive.Core;

public static class NamingGuideEngine
{
    private static readonly NamingGuide Mandatory=new() { BrandPolicies=NamingPolicies.Standard() };
    public static NamingDecision Generate(CarIdentity car,WorkspaceState state,NamingGuide? guide=null)
    {
        var warnings=new List<string>();
        var brand=BuiltInNamingGuide.ResolveBrand(car,state,guide ?? Mandatory);
        var policy=NamingPolicies.For(brand,guide ?? Mandatory) ?? NamingPolicies.For(brand,Mandatory);
        var model=LatinText.English(NamingText.FullModel(car));
        var family=NamingText.Family(brand,car.Series,model);
        var generation=NamingText.Generation(car.Generation);
        if(generation.Length==0)warnings.Add("代次字段未确定，请补充明确的1st、2nd等世代信息");
        var code=NamingText.Clean(car.Code);
        var phase=NamingText.Phase(car.Phase);
        if(RevisionEvidence.Warning(car) is { } revisionWarning)warnings.Add(revisionWarning);
        var years=NamingText.Years(car.ProductionYears);
        var brandKey=NamingText.Key(brand);
        var b=NamingText.PolicyBrand(brand);
        var entries=guide?.Index.Family(brandKey,family) ?? [];
        if(entries.Count==0 && guide is not null)
        {
            var aliasKey=NamingText.ModelKey(model);
            var aliases=guide.Index.Brand(brandKey).Where(e=>NamingText.ModelKey(e.Model)==aliasKey || e.Aliases.Any(alias=>NamingText.ModelKey(NamingText.StripBrand(alias,car.Brand))==aliasKey)).ToList();
            if(aliases.Select(e=>NamingText.Key(e.Family)).Distinct().Count()==1) { family=aliases[0].Family;entries=guide.Index.Family(brandKey,family); }
        }
        var codeSpellings=entries.Where(e=>code.Length>0 && NamingText.Key(e.Code)==NamingText.Key(code)).Select(e=>e.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if(codeSpellings.Count==1) code=codeSpellings[0];
        var modelKey=NamingText.Key(model);
        var compactModel=NamingText.ModelKey(model);
        var exact=entries.Where(e=>NamingText.ModelKey(e.Model)==compactModel || e.Aliases.Any(alias=>NamingText.ModelKey(NamingText.StripBrand(alias,car.Brand))==compactModel)).ToList();
        if(years.Length==0)
        {
            var ranges=exact.Where(e=>e.ProductionYears.Length>0 && (generation.Length>0 && e.Generation==generation || code.Length>0 && (NamingText.Key(e.Code)==NamingText.Key(code) || NamingText.Key(e.RawCode)==NamingText.Key(code))))
                .Select(e=>e.ProductionYears).Distinct().ToList();
            if(ranges.Count==1) years=ranges[0];
        }
        var aliasGeneration=entries.Where(e=>code.Length>0 && (NamingText.Key(e.RawCode)==NamingText.Key(code) || NamingText.Key(e.Code)==NamingText.Key(code)))
            .Where(e=>e.Generation.Length>0).ToList();
        var porsche911= b=="porsche" && family.Equals("911",StringComparison.OrdinalIgnoreCase);

        if(b=="audi")
        {
            var baseRows=aliasGeneration.Where(e=>Regex.IsMatch(e.Model,@"^"+Regex.Escape(family)+@"(?:\b|\s)",RegexOptions.IgnoreCase)).ToList();
            var mapped=(baseRows.Count>0?baseRows:aliasGeneration).GroupBy(e=>e.Generation).OrderByDescending(g=>g.Count()).ThenByDescending(g=>g.Max(e=>e.ConfirmationOrder)).FirstOrDefault()?.Key;
            var derivative=Regex.IsMatch(model,@"^(?:RS\s*\d|S\s*\d|RS\s*Q|SQ|TT\s*(?:RS|S)\b)",RegexOptions.IgnoreCase);
            if(mapped is not null) generation=mapped;
            else if(derivative && !Regex.IsMatch(family,@"^A[4-8]$")) { generation="";warnings.Add("缺少对应基础车系代次映射，请核对"); }
            model=NamingText.WithoutDrivetrain(model,b);
            model=Regex.Replace(model,@"\bRS\s+(\d)","RS$1",RegexOptions.IgnoreCase);
            model=Regex.Replace(model,@"\b([AS]\d)\s+L\b","$1L",RegexOptions.IgnoreCase);
        }

        var profile=guide?.Index.Profiles(brandKey,family).Where(p=>(p.Generation.Length==0 || p.Generation==generation)
                && (p.Model.Length==0 || p.Model==modelKey || exact.Any(e=>NamingText.Key(e.Model)==p.Model)))
            .OrderByDescending(p=>(p.Model.Length>0?2:0)+(p.Generation.Length>0?1:0)).ThenByDescending(p=>p.Votes).FirstOrDefault();
        var style=profile is not null?profile.Style:"{generation} {phase}";
        var reason=profile is null?"默认：完整型号＋代次／改款":profile.Id+" · "+profile.Brand+" / "+profile.Family+" · "+profile.Resolution;
        style=LocalNamingLearning.SuffixStyle(car,state,style,out var learnedSuffix);
        if(learnedSuffix.Length>0) reason=learnedSuffix;
        if(policy?.Suffix=="none") { style="";reason+="；明确补充：不添加代次／代号括号"; }
        else if(policy?.Suffix is { Length:>0 } requiredSuffix) { style=requiredSuffix;reason+="；明确补充：使用代号"; }
        if(policy?.ModelConvention=="Porsche" && family is "718" or "Cayman" or "Boxster")
        { style="{code} {phase}";reason+="；Porsche 跑车车系使用代号"; }
        // Historical aliases select suffix policy only. They cannot shorten the full designation,
        // reintroduce a brand prefix, or discard verified trim/special-edition information.

        if(b=="audi" && Regex.IsMatch(family,@"^A[4-8]$"))
        {
            var expected=family is "A4" or "A5"?"B":family is "A6" or "A7"?"C":"D";
            var common=Regex.IsMatch(code,@"^"+expected+@"\d+$",RegexOptions.IgnoreCase)?code.ToUpperInvariant():"";
            if(common.Length==0)
            {
                var candidates=entries.Where(e=>Regex.IsMatch(e.Code,@"^"+expected+@"\d+$",RegexOptions.IgnoreCase)
                    && (NamingText.Key(e.RawCode)==NamingText.Key(code) && code.Length>0 || generation.Length>0 && e.Generation==generation)).Select(e=>e.Code.ToUpperInvariant()).Distinct().ToList();
                if(candidates.Count==1) common=candidates[0];
            }
            code=common;style="{code} {phase}";reason+="；Audi 补充要求：B/C/D 代号";
        }
        else if(b=="audi") { style="{generation} {phase}";reason+="；Audi 补充要求：基础车系代次"; }

        if(profile?.PhaseWord=="LCI") phase=phase.Replace("Facelift","LCI",StringComparison.Ordinal);
        if(b=="bmw") { phase=phase.Replace("Facelift","LCI",StringComparison.Ordinal);reason+="；BMW 补充要求：LCI"; }
        if(b=="rolls royce")
        {
            if(family.Equals("Phantom",StringComparison.OrdinalIgnoreCase))
            { if(aliasGeneration.Select(e=>e.Generation).Distinct().Count()==1 && aliasGeneration.Count>0) { var mapped=aliasGeneration[0].Generation;if(generation.Length>0 && generation!=mapped) warnings.Add("Phantom代号对应的指南代次与返回字段不同，请核对");generation=mapped; }style="{roman} {phase}";reason+="；Phantom 保留罗马数字代次"; }
            else if(Regex.IsMatch(family,@"^Silver\b|^Corniche$|^Camargue$|^Twenty$|^Wraith \d",RegexOptions.IgnoreCase) || years.Length>0 && int.Parse(years[..4])<2003)
            { style="{years} {phase}";reason+="；老款 Rolls-Royce 使用生产年份范围"; }
            else { style="{generation} {phase}";reason+="；现代 Rolls-Royce 代次"; }
        }

        if(porsche911)
        {
            var revised=NamingText.Porsche911Code(code,car.Phase,out var conflict);
            if(revised.Length>0)
            {
                code=revised;style="{code}";phase="";
                if(conflict) warnings.Add("911 代号与改款字段冲突，按明确改款字段命名，请人工核对");
                reason+="；Porsche 911 中改前使用底盘代号，中改后使用 .2 代号";
            }
            else if(code.Length==0) { style="{generation} {phase}";warnings.Add("911 代号未确定，未推测 .1/.2"); }
        }

        style=NamingSupplementRules.Suffix(brand,family,model)??style;
        if(b=="chevrolet" && family.Equals("Corvette",StringComparison.OrdinalIgnoreCase))model=NamingText.Clean(Regex.Replace(model,@"\bCoupe\b","",RegexOptions.IgnoreCase));
        if(b=="mazda")model=Regex.Replace(model,@"^(?:Mazda\s*)?([236])(?=\s|$)","Mazda$1",RegexOptions.IgnoreCase);
        if(b=="toyota" && Regex.IsMatch(model,@"^(?:Land Cr[ui]+ser|Prado)\b",RegexOptions.IgnoreCase))
        {
            style="{phase}";
            var seriesNumber=Regex.Match(model,@"\b(?:40|60|70|80|90|100|120|150|200|250|300)\b");
            if(!seriesNumber.Success)seriesNumber=Regex.Match(code,@"(?:40|60|70|80|90|100|120|150|200|250|300)(?=[A-Z]?$)",RegexOptions.IgnoreCase);
            if(seriesNumber.Success)
            {
                model=Regex.Replace(model,@"^(?:Land Cr[ui]+ser(?: Prado)?|Prado)(?:\s+"+seriesNumber.Value+@")?","Land Cruiser "+seriesNumber.Value,RegexOptions.IgnoreCase);
            }
        }
        if(b=="jaguar" && Regex.IsMatch(model,@"^XJ",RegexOptions.IgnoreCase))
        {
            var legacyGeneration=code.ToUpperInvariant() switch {"XJ40"=>"4th","X300"=>"5th","X308"=>"6th","X350" or "X358"=>"7th","X351"=>"8th",_=>""};
            if(legacyGeneration.Length>0){generation=legacyGeneration;reason+="；Jaguar补充要求：XJ计入历史世代";}
        }
        var localModel=LocalModelSpelling.Resolve(NamingPolicies.Model(model,family,policy),brand,family,code,generation,guide);
        if(localModel is not null){model=localModel;reason+="；沿用已收录车型用词";}

        // Vocabulary rules are local and cannot change the chosen suffix policy.
        var rules=state.Rules.Select((rule,index)=>(rule,index)).Where(x=>x.rule.Enabled && x.rule.From.Length>0
            && (x.rule.Brand.Length==0 || NamingText.Key(x.rule.Brand)==NamingText.Key(car.Brand) || NamingText.Key(x.rule.Brand)==b))
            .OrderByDescending(x=>x.rule.Brand.Length>0).ThenByDescending(x=>x.index).GroupBy(x=>NamingText.Key(x.rule.From)).Select(g=>g.First().rule).ToList();
        if(rules.Count>0)
        {
            var map=rules.ToDictionary(rule=>rule.From,StringComparer.OrdinalIgnoreCase);
            var pattern=@"(?<!\w)(?:"+string.Join("|",map.Keys.OrderByDescending(x=>x.Length).Select(Regex.Escape))+@")(?!\w)";
            model=Regex.Replace(model,pattern,match=>map[match.Value].To,RegexOptions.IgnoreCase);
        }
        model=LocalNamingLearning.VocabularyModel(car,state,model,out var learnedVocabulary);
        if(learnedVocabulary.Length>0) reason+="；"+learnedVocabulary;
        model=NamingText.StripBrand(NamingText.WithoutDrivetrain(model,car.Brand),LatinText.English(car.Brand));
        if(b=="audi")
        {
            // Mandatory conventions also run after legacy spelling/package rules, so old names cannot undo them.
            model=Regex.Replace(model,@"\bRS\s+(\d)","RS$1",RegexOptions.IgnoreCase);
            model=Regex.Replace(model,@"\b([AS]\d)\s+L\b","$1L",RegexOptions.IgnoreCase);
            model=Regex.Replace(model,@"\bS[ -]line\b","S-line",RegexOptions.IgnoreCase);
        }
        if(b=="bmw") model=Regex.Replace(model,"Coupé","Coupe",RegexOptions.IgnoreCase);
        model=NamingPolicies.Model(model,family,policy);
        model=NamingText.RemoveRepeatedBodyTerms(model);
        var number=Regex.Match(generation,@"^\d+").Value;
        var roman=number.Length>0?NamingText.Roman(int.Parse(number)):"";
        phase=Regex.Replace(phase,@"\b(\d+(?:st|nd|rd|th))[ _]+Facelift\b","$1_Facelift",RegexOptions.IgnoreCase);
        var values=new Dictionary<string,string> { ["generation"]=generation,["number"]=number,["roman"]=roman,["code"]=code,["phase"]=phase,["years"]=years };
        // If a required identity is missing, do not emit a misleading phase-only parenthesis.
        var primary=Regex.Matches(style,@"\{(generation|number|roman|code|years)\}").Select(m=>m.Groups[1].Value).ToList();
        if(primary.Any(key=>values[key].Length==0))
        {
            warnings.Add("命名所需字段未确定："+string.Join("、",primary.Where(key=>values[key].Length==0).Distinct()));
            style=primary.Contains("years") && years.Length>0?"{years} {phase}":primary.Contains("generation") && generation.Length>0?"{generation} {phase}":primary.Contains("roman") && roman.Length>0?"{roman} {phase}":primary.Contains("code") && code.Length>0?"{code} {phase}":"";
        }
        var suffix=Regex.Replace(style,@"\{(\w+)\}",m=>values.GetValueOrDefault(m.Groups[1].Value,""));
        suffix=Regex.Replace(NamingText.Clean(suffix),@"\s+,",",").Trim(' ', ',');
        if(model.Length==0) { warnings.Add("完整型号为空，请人工补充");return new("",reason,warnings); }
        var generated=new NamingDecision(LatinText.English(NamingText.Clean(model+(suffix.Length>0?" ("+suffix+")":""))),reason,warnings);
        var remembered=LocalNamingLearning.ApplyExact(car,state,generated);
        // Explicit supplements cannot be bypassed by an old exact-name memory.
        if(policy?.Suffix.Length>0 || policy?.ModelConvention is "AMG" or "MercedesFullSeries" || policy?.ModelConvention=="Porsche" && family is "718" or "911" or "Cayman" or "Boxster")
        {
            var oldParts=Regex.Match(remembered.Name,@"^(.*?)(?:\s+\(([^()]*)\))?$");
            if(oldParts.Groups[2].Value!=suffix || NamingPolicies.Model(oldParts.Groups[1].Value,family,policy)!=oldParts.Groups[1].Value) return generated;
        }
        return remembered;
    }

}
