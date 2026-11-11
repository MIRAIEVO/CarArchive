using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace CarArchive.Core;

public static partial class GroupingService
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int StrCmpLogicalW(string a, string b);
    public static List<Photo> Sort(IEnumerable<Photo> photos) => photos
        .OrderBy(p=>p.CapturedAt.HasValue?0:1).ThenBy(p=>p.CapturedAt)
        .ThenBy(p=>p.Name,Comparer<string>.Create((a,b)=>OperatingSystem.IsWindows()?StrCmpLogicalW(a,b):StringComparer.OrdinalIgnoreCase.Compare(a,b))).ToList();
    public static List<PhotoGroup> Suggest(IEnumerable<Photo> photos, int gapSeconds)
    {
        var result = new List<PhotoGroup>(); Photo? previous = null;
        foreach (var photo in Sort(photos))
        {
            // Unknown timestamps stay separate: adjacency alone is not evidence of the same car.
            var split = previous is null || !photo.CapturedAt.HasValue || !previous.CapturedAt.HasValue || (photo.CapturedAt.Value - previous.CapturedAt.Value).TotalSeconds > gapSeconds;
            if (split) result.Add(new() { State = "时间分组建议 · 请确认" });
            result[^1].Photos.Add(photo); previous = photo;
        }
        return result;
    }
    public static List<PhotoGroup> FromVisual(IReadOnlyList<Photo> photos, List<List<string>> proposed)
    {
        var lookup = photos.ToDictionary(p => p.Id); var used = new HashSet<string>(); var groups = new List<PhotoGroup>();
        foreach (var ids in proposed)
        {
            var selected = ids.Where(id => lookup.ContainsKey(id) && used.Add(id)).Select(id => lookup[id]).ToList();
            if (selected.Count > 0) groups.Add(new() { Photos = new ObservableCollection<Photo>(selected), State = "视觉分组建议 · 请确认" });
        }
        foreach (var p in photos.Where(p => !used.Contains(p.Id))) groups.Add(new() { Photos = [p], State = "未能分组 · 请检查" });
        return groups;
    }
}
