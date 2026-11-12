namespace CarArchive.Core;

public static class GroupPhotoEditor
{
    public static IReadOnlyList<Photo> Validate(PhotoGroup group,IReadOnlyList<Photo> selected)
    {
        if(!group.Editable) throw new InvalidOperationException("本组正在执行任务或已归档，不能编辑照片。");
        var chosen=selected.Distinct().ToHashSet();
        if(chosen.Count==0 || chosen.Any(p=>!group.Photos.Contains(p))) throw new InvalidOperationException("请重新选择本组中的照片。");
        return group.Photos.Where(chosen.Contains).ToArray();
    }
    public static PhotoGroup Copy(PhotoGroup group,IReadOnlyList<Photo> selected)
    {
        var chosen=Validate(group,selected);
        var copy=new PhotoGroup { Photos=new(chosen.Select(p=>p.CopyReference(true))),UserTouched=true,State="复制组 · 可框选另一辆车" };
        var representative=group.Photos.FirstOrDefault(p=>p.Id==group.RepresentativeId)?.AssetId;
        copy.RepresentativeId=copy.Photos.FirstOrDefault(p=>p.AssetId==representative)?.Id ?? copy.Photos[0].Id;
        return copy;
    }
}
