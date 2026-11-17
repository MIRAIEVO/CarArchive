namespace CarArchive.Core;

public static class GroupMergePolicy
{
    /// <summary>Input order is the workspace order. Equal priorities keep the earliest group.</summary>
    public static PhotoGroup Choose(IReadOnlyList<PhotoGroup> ordered)
    {
        if(ordered.Count==0) throw new ArgumentException("请选择分组。",nameof(ordered));
        return ordered.OrderByDescending(HasFields).ThenByDescending(group=>group.Suggestion is not null || group.NamingOriginalIdentity is not null).First();
    }

    private static bool HasFields(PhotoGroup group)
    {
        var car=group.Confirmed;
        string[] fields=[car.Brand,car.Series,car.Variant,car.Body,car.Generation,car.Code,car.Phase,car.Package,car.OfficialName,car.ProductionYears,group.FolderName,group.TargetBrand];
        return fields.Any(value=>!string.IsNullOrWhiteSpace(value));
    }
}
