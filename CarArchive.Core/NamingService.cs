using System.Text.RegularExpressions;

namespace CarArchive.Core;

public sealed class NamingService
{
    public static string DefaultName(CarIdentity car)
    {
        var model = string.IsNullOrWhiteSpace(car.Variant) ? car.Series : car.Variant;
        var parts = new[] { model, car.Body, car.Package }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        var suffix = new List<string>();
        if (car.Code.Length > 0) suffix.Add(car.Code); else if (car.Generation.Length > 0) suffix.Add(car.Generation);
        if (car.Phase.Length > 0 && !car.Phase.Equals("Pre-Facelift", StringComparison.OrdinalIgnoreCase)) suffix.Add(car.Phase);
        return Regex.Replace(string.Join(" ", parts) + (suffix.Count > 0 ? $" ({string.Join(" ", suffix)})" : ""), @"\s+", " ").Trim();
    }
    public (string Name, string Reason) Suggest(CarIdentity identity, WorkspaceState state, IEnumerable<string> existing)
    {
        var remembered = state.Names.LastOrDefault(n => n.IdentityKey == identity.Key);
        if (remembered is not null) return (remembered.FolderName, "已确认的车型名称");
        var name = DefaultName(identity); var applied = new List<string>();
        var rules=state.Rules.Select((r,i)=>(Rule:r,Index:i))
            .Where(x=>x.Rule.Enabled && x.Rule.From.Length>0 && (x.Rule.Brand.Length==0 || x.Rule.Brand.Equals(identity.Brand,StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x=>x.Rule.Brand.Length>0).ThenByDescending(x=>x.Index)
            .GroupBy(x=>x.Rule.From,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First().Rule,StringComparer.OrdinalIgnoreCase);
        if(rules.Count>0)
        {
            var pattern=@"(?<!\w)(?:"+string.Join("|",rules.Keys.OrderByDescending(s=>s.Length).Select(Regex.Escape))+@")(?!\w)";
            name=Regex.Replace(name,pattern,match=> { var rule=rules[match.Value]; applied.Add(rule.Description); return rule.To; },RegexOptions.IgnoreCase);
        }
        var match = existing.FirstOrDefault(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));
        return (match ?? name, match is not null ? "匹配已有目录" : string.Join("；", applied.DefaultIfEmpty("默认英文命名")));
    }
    public void Remember(CarIdentity identity, string folder, WorkspaceState state)
    {
        state.Names.RemoveAll(n => n.IdentityKey == identity.Key);
        state.Names.Add(new(identity.Key, identity.Brand, folder));
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
