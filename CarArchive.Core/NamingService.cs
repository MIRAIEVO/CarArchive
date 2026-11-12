using System.Text.RegularExpressions;

namespace CarArchive.Core;

public sealed class NamingService
{
    public NamingGuide? Guide { get; set; }
    public static string DefaultName(CarIdentity car)
    {
        return NamingGuideEngine.Generate(car,new()).Name;
    }
    public string ResolveBrand(string brand,WorkspaceState state)=>Guide?.Index.ResolveBrand(brand,state) ?? NamingGuideBuilder.MapBrand(brand,state);
    public (string Name, string Reason) Suggest(CarIdentity identity, WorkspaceState state, IEnumerable<string> existing)
    {
        var guide=Guide is not null && (Guide.SourceRoot.Length==0 || Guide.SourceRoot.Equals(state.Settings.ArchiveRoot,StringComparison.OrdinalIgnoreCase))?Guide:null;
        var decision=NamingGuideEngine.Generate(identity,state,guide);var name=decision.Name;
        var match = existing.FirstOrDefault(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));
        return (match ?? name, decision.Reason+(match is not null?"；匹配规范名称的已有目录":""));
    }
    public void Remember(CarIdentity identity, string folder, WorkspaceState state)
    {
        state.Names.RemoveAll(n => n.IdentityKey == identity.Key || n.IdentityKey == identity.NamingKey);
        state.Names.Add(new(identity.NamingKey, identity.Brand, folder));
    }
    public NamingRule? ProposeRule(string before, string after, string brand)
    {
        if (before == after || before.Length == 0 || after.Length == 0) return null;
        // Learn vocabulary only. Do not generalize chassis numbers, generations or trim levels.
        string[] vocabulary = ["Station Wagon", "Shooting Brake", "Sportback", "Hatchback", "Cabriolet", "Convertible", "Coupe", "Coupé", "Estate", "Wagon", "Sedan", "Saloon", "Facelift", "Pre-Facelift", "AMG-Line", "AMG Line", "S-line", "S line"];
        foreach (var from in vocabulary.OrderByDescending(v => v.Length))
        foreach (var to in vocabulary.OrderByDescending(v => v.Length))
            if (from != to && before.Contains(from, StringComparison.Ordinal) && before.Replace(from, to, StringComparison.Ordinal) == after)
                return new() { Brand = brand, From = from, To = to };
        return null;
    }
    public static string SafeSegment(string input)
    {
        var value = input.Trim();
        if (value.Length == 0 || value is "." or ".." || value.EndsWith('.') || value.EndsWith(' ') || value.Length > 150 || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Any(char.IsControl))
            throw new ArgumentException("目录名不能为空、过长或包含 Windows 不允许的字符。");
        var stem = value.Split('.')[0].ToUpperInvariant();
        if (Regex.IsMatch(stem, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$")) throw new ArgumentException("目录名不能使用 Windows 保留名称。");
        return value;
    }
    public static List<string> ExistingNames(string root, string brand)
    {
        if (!Directory.Exists(root) || brand.Length == 0) return [];
        var directory = Path.Combine(root, SafeSegment(brand));
        return Directory.Exists(directory) ? Directory.EnumerateDirectories(directory).Select(p => Path.GetFileName(p)).ToList() : [];
    }
}
