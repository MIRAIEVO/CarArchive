using System.Text.Json;
using System.Text.RegularExpressions;

namespace CarArchive.Core;

/// <summary>The shipped corpus is naming evidence, never a catalog of physical archive endpoints.</summary>
public static class BuiltInNamingGuide
{
    private static readonly Lazy<NamingGuide> Shipped = new(() =>
    {
        using var stream=typeof(BuiltInNamingGuide).Assembly.GetManifestResourceStream("CarArchive.Core.naming-guide.json")
            ?? throw new InvalidDataException("程序内置命名指南缺失，请重新解压完整发布包。");
        var guide=JsonSerializer.Deserialize<NamingGuide>(stream,StateStore.Json) ?? throw new InvalidDataException("程序内置命名指南无法读取。");
        guide.SourceRoot="";
        return guide;
    });
    public static NamingGuide Load() => Shipped.Value;
    public static NamingGuide Compose(NamingGuide? local)
    {
        var shipped=Load();if(local is null) return shipped;
        // Local snapshots augment the versioned corpus instead of replacing missing/deleted directories.
        var entries=shipped.Inventory.Concat(local.Inventory).GroupBy(e=>(NamingText.Key(e.Brand),e.Folder.ToUpperInvariant())).Select(g=>g.Last()).ToList();
        var profiles=shipped.Profiles.Concat(local.Profiles).GroupBy(p=>(NamingText.Key(p.Brand),NamingText.Key(p.Family),p.Model,p.Generation)).Select(g=>g.Last()).ToList();
        return new() { SourceRoot="",SourceStateHash=local.SourceStateHash,Inventory=entries,Profiles=profiles,
            BrandPolicies=shipped.BrandPolicies,Notes=shipped.Notes.Concat(local.Notes).Distinct().ToList(),Warnings=local.Warnings.ToList() };
    }
    public static string ResolveBrand(CarIdentity car,WorkspaceState state,NamingGuide? guide=null)
    {
        var brand=car.Brand;
        if(NamingText.PolicyBrand(brand)=="mercedes")
        {
            // Only explicit, confirmed marketing prefixes/sub-brands select a performance division.
            // An AMG-Line package is not evidence of a Mercedes-AMG model.
            var designation=NamingText.Clean(car.OfficialName.Length>0?car.OfficialName:car.Variant);
            if(Regex.IsMatch(designation,@"^(?:Mercedes[ -])?Maybach\b",RegexOptions.IgnoreCase) || NamingText.Key(brand).Contains("maybach")) brand="Mercedes-Maybach";
            else if(Regex.IsMatch(designation,@"^Mercedes[ -]AMG\b",RegexOptions.IgnoreCase) || NamingText.Key(brand)=="mercedes amg" || NamingText.Key(brand)=="amg") brand="Mercedes-AMG";
            else brand="Mercedes-Benz";
        }
        if(NamingText.PolicyBrand(brand)=="mercedes" && state.BrandFolders.TryGetValue(brand,out var mapped)
            && NamingText.PolicyBrand(mapped)=="mercedes" && NamingPolicies.CanonicalBrand(mapped)!=NamingPolicies.CanonicalBrand(brand))
            return (guide ?? Load()).Index.ResolveBrand(brand,new WorkspaceState());
        return (guide ?? Load()).Index.ResolveBrand(brand,state);
    }
    public static NamingGuide PortableSnapshot(NamingGuide source)
    {
        var copy=JsonSerializer.Deserialize<NamingGuide>(JsonSerializer.Serialize(source,StateStore.Json),StateStore.Json)!;
        var root=source.SourceRoot;copy.SourceRoot="";copy.SourceStateHash="";
        string Relative(string value)=>Path.IsPathRooted(value)?Path.GetRelativePath(root,value).Replace('\\','/'):value.Replace('\\','/');
        foreach(var entry in copy.Inventory) entry.Path=Relative(entry.Path);
        foreach(var profile in copy.Profiles) profile.Sources=profile.Sources.Select(Relative).ToList();
        copy.Notes=copy.Notes.Select(n=>n with { Path=Relative(n.Path) }).ToList();
        foreach(var policy in copy.BrandPolicies) policy.Sources=policy.Sources.Select(Relative).ToList();
        var prefix=root.Replace('\\','/').TrimEnd('/');
        if(prefix.Length>0)copy.Warnings=copy.Warnings.Select(w=>w.Replace('\\','/').Replace(prefix+"/","",StringComparison.OrdinalIgnoreCase).Replace(prefix,"",StringComparison.OrdinalIgnoreCase)).ToList();
        return copy;
    }
}

internal static class NamingPolicies
{
    public static string CanonicalBrand(string value)=>NamingText.Key(value) switch
    { "mercedes" or "mercedes benz" or "benz"=>"mercedes benz","mercedes amg" or "amg"=>"mercedes amg","mercedes maybach" or "maybach"=>"mercedes maybach",_=>NamingText.Key(value) };
    public static List<NamingBrandPolicy> Standard() =>
    [
        new() { Brand="Audi",ModelConvention="Audi",Description="基础车系世代；A4/A5/A6/A7/A8及衍生使用B/C/D；RS3、A8L；省略quattro" },
        new() { Brand="BMW",ModelConvention="BMW",Description="按车系/型号指南选后缀；改款LCI；省略xDrive" },
        new() { Brand="Ferrari",Suffix="none",Description="完整型号，不添加代次或代号括号" },
        new() { Brand="Mercedes-Benz",Suffix="{code} {phase}",ModelConvention="MercedesFullSeries",Description="代号；完整车系前缀，如S-Class S500；省略4MATIC" },
        new() { Brand="Mercedes-Maybach",Suffix="{code} {phase}",ModelConvention="MercedesFullSeries",Description="独立Maybach品牌；代号；完整车系前缀，如S-Class S580" },
        new() { Brand="Mercedes-AMG",Suffix="{code} {phase}",ModelConvention="AMG",Description="独立AMG品牌；代号；去重复AMG；不把AMG-Line归为AMG" },
        new() { Brand="Porsche",ModelConvention="Porsche",Description="718/911/Cayman/Boxster使用代号；其他车系默认代次；911中改前基础代号，中改后.2" },
        new() { Brand="Rolls-Royce",ModelConvention="RollsRoyce",Description="现代世代不计入旧车；老车生产年代；Phantom罗马数字" }
    ];
    public static NamingBrandPolicy? For(string brand,NamingGuide guide)
        => guide.BrandPolicies.FirstOrDefault(p=>CanonicalBrand(p.Brand)==CanonicalBrand(brand));
    public static string Model(string model,string family,NamingBrandPolicy? policy)
    {
        if(policy?.ModelConvention=="AMG")
            return NamingText.Clean(Regex.Replace(model,@"(?:^AMG\s+|\s+AMG(?=\s|$)|\bSedan\b)"," ",RegexOptions.IgnoreCase));
        if(policy?.ModelConvention!="MercedesFullSeries" || !family.EndsWith("-Class",StringComparison.OrdinalIgnoreCase)) return model;
        if(model.StartsWith(family+" ",StringComparison.OrdinalIgnoreCase) || model.Equals(family,StringComparison.OrdinalIgnoreCase))
            return family+Regex.Replace(model[family.Length..],@"^\s+([A-Z]{1,3})\s+(\d{2,3})(?=\s|$)"," $1$2",RegexOptions.IgnoreCase);
        if(Regex.IsMatch(model,@"^[A-Z]{1,3}\s*\d{2,3}(?=\s|$)",RegexOptions.IgnoreCase))
            return family+" "+Regex.Replace(model,@"^([A-Z]{1,3})\s+(\d{2,3})(?=\s|$)","$1$2",RegexOptions.IgnoreCase);
        return model;
    }
}
