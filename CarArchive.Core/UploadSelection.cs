namespace CarArchive.Core;
public static class UploadSelection
{
    public static List<Photo> Select(PhotoGroup group, bool? uploadMultiple=null)
    {
        var available=group.Photos.Where(p=>!p.Excluded).ToList();
        if(available.Count==0) throw new InvalidOperationException("本组没有可上传的照片。");
        var current=available.FirstOrDefault(p=>p.Id==group.LastViewedId) ?? available.FirstOrDefault(p=>p.Id==group.RepresentativeId) ?? available[0];
        var chosen=(uploadMultiple ?? group.UploadMultiple)?available.Where(p=>p.Crop is not null).ToList():new List<Photo>{current};
        if(chosen.Count==0) chosen.Add(current);
        return chosen.Select(p=>p.Snapshot()).ToList();
    }
}
