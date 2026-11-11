using System.Text.RegularExpressions;

namespace CarArchive.Core;

public sealed record CategorySuggestion(string BrandDirectory, string FolderName, string TargetPath, bool Exists)
{
    public string Details => BrandDirectory + (Exists ? " · 已有目录" : " · 尚无目录");
    public string PathDisplay => TargetPath.Length > 0 ? TargetPath : "请选择归档根目录";
}

public sealed record CategoryCatalogResult(IReadOnlyList<CategorySuggestion> Items, IReadOnlyList<string> Warnings);

/// <summary>Local, read-only index of archive endpoints. Scanning belongs on a background thread.</summary>
public static class CategoryCatalogService
{
    public static CategoryCatalogResult Scan(string root, IReadOnlyList<LearnedName> names,
        IReadOnlyDictionary<string, string> brandFolders, CancellationToken cancellationToken = default)
    {
        var items = new Dictionary<string, CategorySuggestion>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        root = root.Trim();
        if (root.Length > 0)
        {
            try { root = Path.GetFullPath(root); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            { return new([], ["归档根目录无效：" + error.Message]); }
        }

        void Add(string brand, string folder, bool exists)
        {
            // Use the same segments as archive planning; never reinterpret a remembered name as a path.
            brand = NamingService.SafeSegment(brand);
            folder = NamingService.SafeSegment(folder);
            var target = root.Length == 0 ? "" : Path.GetFullPath(Path.Combine(root, brand, folder));
            var key = target.Length > 0 ? target : brand + "\\" + folder;
            if (!items.ContainsKey(key)) items.Add(key, new(brand, folder, target, exists));
        }

        if (root.Length > 0)
        {
            try
            {
                if (Directory.Exists(root))
                    foreach (var brandPath in Directory.EnumerateDirectories(root))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            foreach (var folderPath in Directory.EnumerateDirectories(brandPath))
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                try { Add(Path.GetFileName(brandPath), Path.GetFileName(folderPath), true); }
                                catch (ArgumentException) { /* Not a valid archive endpoint. */ }
                            }
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                        { warnings.Add("无法读取分类目录 " + brandPath + "：" + error.Message); }
                    }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { warnings.Add("无法读取归档根目录：" + error.Message); }
        }
        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var brand = brandFolders.FirstOrDefault(pair => pair.Key.Equals(name.Brand, StringComparison.OrdinalIgnoreCase)).Value ?? name.Brand;
            try
            {
                // Existing disk spelling wins over a remembered name differing only in case.
                Add(brand, name.FolderName, root.Length > 0 && Directory.Exists(Path.Combine(root, brand, name.FolderName)));
            }
            catch (ArgumentException) { /* Old invalid or incomplete memories cannot be archive targets. */ }
        }
        return new(items.Values.ToArray(), warnings);
    }

    public static IReadOnlyList<CategorySuggestion> Match(IEnumerable<CategorySuggestion> items, string query,
        string currentBrand, int limit = 20)
    {
        query = Normalize(query);
        if (query.Length == 0 || limit <= 0) return [];
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return items.Select(item => new { Item = item, Name = Normalize(item.FolderName), Brand = Normalize(item.BrandDirectory) })
            .Where(x => words.All(word => (x.Name + " " + x.Brand).Contains(word, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.Name.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0 : x.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(x => x.Brand.Equals(Normalize(currentBrand), StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.Item.FolderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Item.BrandDirectory, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Min(limit, 20)).Select(x => x.Item).ToArray();
    }

    public static void Apply(PhotoGroup group, CategorySuggestion suggestion)
    {
        if (!group.Editable) throw new InvalidOperationException("本组正在执行任务或已归档，不能修改归档目标。");
        var name = NamingService.SafeSegment(suggestion.FolderName);
        var brand = NamingService.SafeSegment(suggestion.BrandDirectory);
        group.PreviousFolderName = group.FolderName;
        group.FolderName = name;
        group.TargetBrand = brand;
        group.ManualName = true;
        group.AppliedRule = suggestion.Exists ? "手动选择已有分类" : "手动选择记忆名称 · 尚无目录";
        group.Refresh();
    }

    private static string Normalize(string text) => Regex.Replace(text.Trim(), @"\s+", " ");
}
