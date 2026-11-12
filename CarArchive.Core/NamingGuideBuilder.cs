using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CarArchive.Core;

public static class NamingGuideBuilder
{
    public static NamingGuide Build(string root, WorkspaceState state, IReadOnlyList<ArchiveJob>? jobs = null, CancellationToken ct = default)
    {
        var guide=new NamingGuide { SourceRoot=root.Length>0?System.IO.Path.GetFullPath(root):"",SourceStateHash=NamingGuideStore.SourceStateHash(state,jobs) };
        if(root.Length==0 || !Directory.Exists(root)) return guide;
        var remembered=state.Names.Select((name,index)=>(Name:name,Identity:ReadIdentity(name),Order:index+1)).ToList();
        var dates=(jobs ?? []).Where(job=>job.Status=="Completed" && job.Direction=="Archive").GroupBy(job=>job.GroupId).ToDictionary(g=>g.Key,g=>g.Max(job=>job.CreatedAt));
        var groupOrder=state.Groups.Where(g=>g.Archived).OrderBy(g=>dates.GetValueOrDefault(g.Id)).Select((g,i)=>(Group:g,Order:remembered.Count+i+1)).ToList();
        var brandPaths=Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var brands=brandPaths.ToDictionary(path=>NamingText.Key(System.IO.Path.GetFileName(path)),path=>System.IO.Path.GetFileName(path));
        string Resolve(string brand)
        {
            var mapped=MapBrand(brand,state);
            return brands.GetValueOrDefault(NamingText.Key(mapped)) ?? brands.GetValueOrDefault(NamingText.Key(brand)) ?? mapped;
        }
        static string Endpoint(string brand,string folder)=>brand+"\0"+folder;
        var sourcesByFolder=remembered.Select(item=>(Key:Endpoint(Resolve(item.Name.Brand),item.Name.FolderName),Identity:item.Identity,Order:item.Order))
            .Concat(groupOrder.Select(item=>(Key:Endpoint(Resolve(item.Group.TargetBrand),item.Group.FolderName),Identity:item.Group.Confirmed,Order:item.Order)))
            .ToLookup(item=>item.Key,StringComparer.OrdinalIgnoreCase);
        foreach(var brandPath in brandPaths)
        {
            ct.ThrowIfCancellationRequested();var brand=System.IO.Path.GetFileName(brandPath);
            try
            {
                ReadNotes(brandPath,guide);
                foreach(var path in Directory.EnumerateDirectories(brandPath).Order(StringComparer.OrdinalIgnoreCase))
                {
                    ct.ThrowIfCancellationRequested();var folder=System.IO.Path.GetFileName(path);
                    var sources=sourcesByFolder[Endpoint(brand,folder)].ToList();
                    var source=sources.OrderByDescending(item=>item.Order).FirstOrDefault();
                    var car=source.Identity;
                    var split=Regex.Match(folder,@"^(.*?)(?:\s+\(([^()]*)\))?$");
                    var model=NamingText.StripBrand(NamingText.RemoveRepeatedBodyTerms(split.Groups[1].Value),brand);
                    var entry=new NamingGuideEntry { Brand=brand,Folder=folder,Path=path,Model=model,Suffix=split.Groups[2].Value,
                        HasIdentity=car is not null,ConfirmationOrder=source.Order,RawCode=car?.Code ?? "",Package=car?.Package ?? "",ProductionYears=NamingText.Years(car?.ProductionYears ?? ""),Generation=NamingText.Generation(car?.Generation ?? ""),
                        Family=NamingText.Family(brand,car?.Series ?? "",model) };
                    entry.Aliases.Add(model);
                    foreach(var identity in sources.Select(item=>item.Identity))
                    {
                        var alias=NamingText.StripBrand(NamingText.FullModel(identity),identity.Brand);
                        if(alias.Length>0) entry.Aliases.Add(alias);
                    }
                    entry.Aliases=entry.Aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if(split.Groups[1].Value!=NamingText.RemoveRepeatedBodyTerms(split.Groups[1].Value)) entry.Warning="历史名称含重复车身词，仅作为来源保留";
                    if(Regex.IsMatch(model,@"\b(Convertible|Cabriolet)\b.*\b\1\b",RegexOptions.IgnoreCase)) entry.Warning="历史名称疑似重复车身词，不复制旧完整名称";
                    ParseSuffix(entry,car);
                    ApplyPolicies(entry);
                    guide.Inventory.Add(entry);
                }
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException)
            { guide.Warnings.Add("无法完整读取 "+brandPath+"："+error.Message); }
        }
        ReadNotes(root,guide);
        foreach(var family in guide.Inventory.GroupBy(e=>(Brand:NamingText.Key(e.Brand),Family:NamingText.Key(e.Family))))
        {
            AddProfile(guide,family.ToList(),"","");
            foreach(var generation in family.Where(e=>e.Generation.Length>0).GroupBy(e=>e.Generation)) AddProfile(guide,generation.ToList(),"",generation.Key);
            foreach(var model in family.GroupBy(e=>NamingText.Key(e.Model)))
            {
                AddProfile(guide,model.ToList(),model.Key,"");
                foreach(var generation in model.Where(e=>e.Generation.Length>0).GroupBy(e=>e.Generation)) AddProfile(guide,generation.ToList(),model.Key,generation.Key);
            }
        }
        return guide;
    }
    private static void ApplyPolicies(NamingGuideEntry entry)
    {
        // Compile mandatory policies into the guide too, so the CSV and runtime agree.
        // Historical spelling/prefix/package data remains evidence, never a complete-name template.
        if(entry.Style.Length==0) entry.Style="{generation} {phase}";
        var brand=NamingText.PolicyBrand(entry.Brand);
        if(brand=="audi") entry.Style=Regex.IsMatch(entry.Family,@"^A[4-8]$")?"{code} {phase}":"{generation} {phase}";
        if(brand=="bmw") entry.PhaseWord="LCI";
        if(brand=="rolls royce")
        {
            entry.Style=entry.Family.Equals("Phantom",StringComparison.OrdinalIgnoreCase)?"{roman} {phase}":
                Regex.IsMatch(entry.Family,@"^Silver\b|^Corniche$|^Camargue$|^Twenty$|^Wraith \d",RegexOptions.IgnoreCase) || entry.ProductionYears.Length>0 && int.Parse(entry.ProductionYears[..4])<2003?"{years} {phase}":"{generation} {phase}";
        }
        if(brand=="porsche" && entry.Family=="911")
        {
            entry.Style="{code}";
            var revised=NamingText.Porsche911Code(entry.Code,entry.Phase,out _);
            if(revised.Length>0)
            {
                if(revised.EndsWith(".2",StringComparison.Ordinal)) entry.Phase="Facelift";
                entry.Code=revised;
            }
        }
    }
    public static string MapBrand(string brand,WorkspaceState state) => state.BrandFolders.FirstOrDefault(pair=>pair.Key.Equals(brand,StringComparison.OrdinalIgnoreCase)).Value ?? brand;
    private static CarIdentity ReadIdentity(LearnedName name)
    {
        var p=name.IdentityKey.Split('|');
        if(p.Length==9 && p[0]=="naming-v3") return new() { Brand=name.Brand,Series=p[2],Variant=p[3],Generation=p[4],Code=p[5],Phase=p[6],Package=p[7],ProductionYears=p[8] };
        return p.Length>=8?new() { Brand=name.Brand,Series=p[1],Variant=p[2],Generation=p[4],Code=p[5],Phase=p[6],Package=p[7] }:new() { Brand=name.Brand,Variant=name.FolderName.Split(" (")[0] };
    }
    private static void ReadNotes(string path,NamingGuide guide)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        foreach(var file in Directory.EnumerateFiles(path,"*.txt").Where(file=>System.IO.Path.GetFileName(file).Contains("补充命名",StringComparison.Ordinal)))
        {
            var bytes=File.ReadAllBytes(file);string text;
            if(bytes.Length>1 && bytes[0]==255 && bytes[1]==254) text=Encoding.Unicode.GetString(bytes).TrimStart('\uFEFF');
            else if(bytes.Length>1 && bytes[0]==254 && bytes[1]==255) text=Encoding.BigEndianUnicode.GetString(bytes).TrimStart('\uFEFF');
            else { try { text=new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF'); } catch(DecoderFallbackException) { text=Encoding.GetEncoding("gb18030").GetString(bytes); } }
            guide.Notes.Add(new(file,text,Convert.ToHexString(SHA256.HashData(bytes))));
            var brand=System.IO.Path.GetFileName(path);
            var recognized=brand.Equals("Audi",StringComparison.OrdinalIgnoreCase) && text.Contains("Quattro",StringComparison.OrdinalIgnoreCase) && text.Contains("A4",StringComparison.OrdinalIgnoreCase)
                || brand.Equals("BMW",StringComparison.OrdinalIgnoreCase) && text.Contains("LCI",StringComparison.OrdinalIgnoreCase)
                || brand.Equals("Rolls-Royce",StringComparison.OrdinalIgnoreCase) && text.Contains("现代",StringComparison.Ordinal) && text.Contains("生产",StringComparison.Ordinal);
            if(!recognized) guide.Warnings.Add("补充文件已留存，未识别的自然语言要求需人工更新结构化指南："+file);
        }
    }
    private static void ParseSuffix(NamingGuideEntry e,CarIdentity? identity)
    {
        var suffix=NamingText.Clean(e.Suffix.Replace('_',' '));
        if(Regex.IsMatch(suffix,@"\bLCI\b",RegexOptions.IgnoreCase)) e.PhaseWord="LCI";
        if(suffix.Length==0) { e.Style="";return; }
        var years=Regex.Match(suffix,@"(?:18|19|20)\d{2}\s*[-–]\s*(?:18|19|20)\d{2}");
        if(years.Success) { e.ProductionYears=NamingText.Years(years.Value);suffix=suffix.Replace(years.Value,"{years}"); }
        var mark=Regex.Match(suffix,@"^(Mk|Mark|Series)\s*([IVX]+|\d+)\b",RegexOptions.IgnoreCase);
        if(mark.Success)
        {
            var numeric=int.TryParse(mark.Groups[2].Value,out var n);if(!numeric) n=NamingText.RomanNumber(mark.Groups[2].Value);
            if(e.Generation.Length==0 && n>0) e.Generation=NamingText.Ordinal(n);
            suffix=mark.Groups[1].Value+" "+(numeric?"{number}":"{roman}")+suffix[mark.Length..];
        }
        else if(Regex.Match(suffix,@"^([IVX]+)(?=\s|$)",RegexOptions.IgnoreCase) is { Success:true } roman && NamingText.RomanNumber(roman.Value)>0)
        { e.Generation=NamingText.Generation(roman.Value);suffix="{roman}"+suffix[roman.Length..]; }
        else
        {
            var gen=Regex.Match(suffix,@"\b\d{1,2}(?:st|nd|rd|th)\b",RegexOptions.IgnoreCase);
            if(gen.Success)
            { e.Generation=NamingText.Generation(gen.Value);suffix=suffix[..gen.Index]+"{generation}"+suffix[(gen.Index+gen.Length)..]; }
        }
        e.Phase=NamingText.Phase(suffix);
        suffix=Regex.Replace(suffix,@"(?:\d+(?:st|nd|rd|th)\s+)?(?:Facelift|Faceclift|LCI)|\b(?:Late|later)\b","{phase}",RegexOptions.IgnoreCase);
        var remainder=Regex.Replace(suffix,@"\{[^}]+\}","").Trim(' ', ',');
        if(remainder.Length>0 && !mark.Success && remainder!="EV" && !remainder.Equals("Crown",StringComparison.OrdinalIgnoreCase))
        {
            if(!Regex.IsMatch(remainder,@"\d|^[A-Z]{1,4}$|^G-(?:Series|Model)$") && (identity is null || NamingText.Key(identity.Code)!=NamingText.Key(remainder)))
            {
                e.Warning+=(e.Warning.Length>0?"；":"")+"旧后缀限定词无法安全映射为代号，采用默认格式待复核："+remainder;
                e.Style="{generation} {phase}";return;
            }
            e.Code=remainder;
            suffix=suffix.Replace(remainder,"{code}",StringComparison.Ordinal);
        }
        if(e.RawCode.Length==0) e.RawCode=e.Code;
        if(!suffix.Contains('{')) { e.Code=e.Suffix;e.RawCode=e.Code;suffix="{code}"; }
        if(!suffix.Contains("{phase}",StringComparison.Ordinal)) suffix+=" {phase}";
        e.Style=NamingText.Clean(suffix);
    }
    private static void AddProfile(NamingGuide guide,List<NamingGuideEntry> entries,string model,string generation)
    {
        var candidates=entries.GroupBy(e=>e.Style)
            .Select(g=>new { Key=g.Key,Entries=g.ToList(),Votes=g.Count(),Latest=g.Max(e=>e.ConfirmationOrder) }).OrderByDescending(g=>g.Votes).ThenByDescending(g=>g.Latest).ToList();
        var winner=candidates[0];var tied=candidates.Count>1 && candidates[1].Votes==winner.Votes;
        var unresolved=tied && candidates[1].Latest==winner.Latest;
        var source=entries[0];
        var phaseWord=entries.Where(e=>Regex.IsMatch(e.Suffix,@"Facelift|Faceclift|\bLCI\b",RegexOptions.IgnoreCase)).GroupBy(e=>e.PhaseWord)
            .OrderByDescending(g=>g.Count()).ThenByDescending(g=>g.Max(e=>e.ConfirmationOrder)).FirstOrDefault()?.Key ?? "Facelift";
        guide.Profiles.Add(new() { Id="profile-"+(guide.Profiles.Count+1).ToString("D4"),Brand=source.Brand,Family=source.Family,Model=model,Generation=generation,
            Style=unresolved?"{generation} {phase}":winner.Key,PhaseWord=phaseWord,Votes=winner.Votes,
            Resolution=(unresolved?"同票且无可区分确认记录，采用默认格式":candidates.Count==1?"适用范围内格式一致":tied?"同票，采用最近软件确认格式":"采用目录数最多的格式")
                +(entries.Any(e=>e.Warning.Contains("无法安全映射",StringComparison.Ordinal))?"；旧后缀限定词待复核":""),
            Sources=entries.Select(e=>e.Path).ToList() });
    }
}
