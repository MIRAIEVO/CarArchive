namespace CarArchive.Core;

/// <summary>Read-only lookup tables shared by all decisions for one guide snapshot.</summary>
internal sealed class NamingGuideIndex
{
    private readonly Dictionary<string,NamingGuideEntry[]> brands;
    private readonly Dictionary<(string Brand,string Family),NamingGuideEntry[]> families;
    private readonly Dictionary<(string Brand,string Family),NamingProfile[]> profiles;
    internal IReadOnlyDictionary<string,NamingGuideEntry> Paths { get; }
    public NamingGuideIndex(NamingGuide guide)
    {
        brands=guide.Inventory.GroupBy(e=>NamingText.Key(e.Brand)).ToDictionary(g=>g.Key,g=>g.ToArray());
        families=guide.Inventory.GroupBy(e=>(NamingText.Key(e.Brand),NamingText.Key(e.Family))).ToDictionary(g=>g.Key,g=>g.ToArray());
        profiles=guide.Profiles.GroupBy(p=>(NamingText.Key(p.Brand),NamingText.Key(p.Family))).ToDictionary(g=>g.Key,g=>g.ToArray());
        Paths=guide.Inventory.ToDictionary(e=>e.Path,StringComparer.OrdinalIgnoreCase);
    }
    public string ResolveBrand(string brand,WorkspaceState state)
    {
        var mapped=NamingGuideBuilder.MapBrand(brand,state);
        if(brands.TryGetValue(NamingText.Key(mapped),out var known)) return known[0].Brand;
        return brands.TryGetValue(NamingText.Key(brand),out known)?known[0].Brand:mapped;
    }
    public IReadOnlyList<NamingGuideEntry> Brand(string key)=>brands.GetValueOrDefault(key) ?? [];
    public IReadOnlyList<NamingGuideEntry> Family(string brand,string family)=>families.GetValueOrDefault((brand,NamingText.Key(family))) ?? [];
    public IReadOnlyList<NamingProfile> Profiles(string brand,string family)=>profiles.GetValueOrDefault((brand,NamingText.Key(family))) ?? [];
}
