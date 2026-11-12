namespace CarArchive.Core;

public sealed record CategorySuggestion(string BrandDirectory, string FolderName, string TargetPath, bool Exists)
{
    public string DisplayName => FolderName+" / "+BrandDirectory;
    public string BrandLabel => " / "+BrandDirectory;
}

public sealed record CategoryCatalogResult(IReadOnlyList<CategorySuggestion> Items, IReadOnlyList<string> Warnings, IReadOnlyList<string>? BrandDirectories=null);

/// <summary>Local, read-only index of archive endpoints. Scanning belongs on a background thread.</summary>
public static class CategoryCatalogService
{
    public static CategoryCatalogResult Scan(string root, IReadOnlyList<LearnedName> names,
        IReadOnlyDictionary<string, string> brandFolders, CancellationToken cancellationToken = default)
        => Scan(root,cancellationToken); // Compatibility: memories are never archive endpoints.

    public static CategoryCatalogResult Scan(string root,CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = new Dictionary<string, CategorySuggestion>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var brands=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        root = root.Trim();
        if (root.Length > 0)
        {
            try { root = Path.GetFullPath(root); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            { return new([], ["归档根目录无效：" + error.Message]); }
        }

        void Add(string brand, string folder, bool exists)
        {
            // Only names that can address the same physical directory through archive planning are eligible.
            if(NamingService.SafeSegment(brand)!=brand || NamingService.SafeSegment(folder)!=folder) return;
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
                            var brand=Path.GetFileName(brandPath);
                            try { if(NamingService.SafeSegment(brand)==brand) brands.Add(brand);else continue; }
                            catch(ArgumentException) { continue; }
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
        return new(items.Values.ToArray(), warnings,brands.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static IReadOnlyList<CategorySuggestion> Match(IEnumerable<CategorySuggestion> items, string query,
        string currentBrand, int limit = 20)
    {
        return new CategorySearchIndex(items).Match(query,currentBrand,limit);
    }

    public static void Apply(PhotoGroup group, CategorySuggestion suggestion)
    {
        if (!group.Editable) throw new InvalidOperationException("本组正在执行任务或已归档，不能修改归档目标。");
        if (!suggestion.Exists) throw new InvalidOperationException("只能选择实际存在的分类目录。");
        var name = NamingService.SafeSegment(suggestion.FolderName);
        var brand = NamingService.SafeSegment(suggestion.BrandDirectory);
        group.PreviousFolderName = group.FolderName;
        group.FolderName = name;
        group.TargetBrand = brand;
        group.ManualName = true;
        group.AppliedRule = "手动选择已有分类";
        group.Refresh();
    }

}
