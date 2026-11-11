using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace CarArchive.Core;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Notify(name); return true;
    }
    public void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public enum RecognitionMode { Accurate, Fast, Economy, FieldsOnly }
public enum SearchPreference { Automatic, Web, ImageReference }
public enum GroupTaskState { Idle, Queued, Running, Completed, Failed, Canceled }
public enum LearningMode { ConfirmRules, Automatic, Manual }
public sealed record CropRect(double X, double Y, double Width, double Height);

public sealed class ImageAsset : Observable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OriginalPath { get; set; } = "";
    public long Length { get; set; }
    public long ModifiedTicks { get; set; }
    public string Hash { get; set; } = "";
    public string PreviewPath { get; set; } = "";
    public string ThumbnailPath { get; set; } = "";
    public DateTimeOffset? CapturedAt { get; set; }
    public string Error { get; set; } = "";
    public void Refresh() => Notify("");
}
public class PhotoReference : Observable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AssetId { get; set; } = "";
    [JsonIgnore] public ImageAsset? Asset { get; private set; }
    public string OriginalPath { get; set; } = "";
    public string Path { get; set; } = "";
    public DateTimeOffset? CapturedAt { get; set; }
    private string previewPath = "", thumbnailPath = "", error = "";
    public string PreviewPath { get => Asset?.PreviewPath ?? previewPath; set { previewPath=value; if(Asset is not null) Asset.PreviewPath=value; Notify(); } }
    public string ThumbnailPath { get => Asset?.ThumbnailPath ?? thumbnailPath; set { thumbnailPath=value; if(Asset is not null) Asset.ThumbnailPath=value; Notify(nameof(ThumbnailUri)); } }
    public string Error { get => Asset?.Error ?? error; set { error=value; if(Asset is not null) Asset.Error=value; Notify(nameof(Caption)); } }
    private bool excluded;
    public bool Excluded { get => excluded; set { if(Set(ref excluded,value)) { Notify(nameof(Caption)); Notify(nameof(ThumbnailOpacity)); } } }
    private CropRect? crop;
    public CropRect? Crop { get => crop; set { if(Set(ref crop,value)) Notify(nameof(Caption)); } }
    [JsonIgnore] public string Name => System.IO.Path.GetFileName(Path);
    [JsonIgnore] public string Caption => Name + (Crop is null ? "" : " · 已框选") + (Excluded ? " · 已排除" : "") + (Error.Length == 0 ? "" : PreviewPath.Length>0?" · 原图需检查":" · 无法预览");
    [JsonIgnore] public double ThumbnailOpacity => Excluded ? 0.35 : 1;
    [JsonIgnore] public string? ThumbnailUri => ThumbnailPath.Length == 0 ? null : new Uri(ThumbnailPath).AbsoluteUri;
    public void Attach(ImageAsset asset)
    {
        if(Asset is not null) Asset.PropertyChanged-=AssetChanged;
        Asset=asset; AssetId=asset.Id; Asset.PropertyChanged+=AssetChanged;
        CapturedAt=asset.CapturedAt ?? CapturedAt; AssetChanged(this,new(""));
    }
    private void AssetChanged(object? sender,PropertyChangedEventArgs e) { CapturedAt=Asset?.CapturedAt ?? CapturedAt; Notify(nameof(ThumbnailUri)); Notify(nameof(PreviewPath)); Notify(nameof(Caption)); }
}
public sealed class Photo : PhotoReference
{
    public Photo Snapshot() { var copy=CopyReference(false,false);copy.Id=Id;return copy; }
    public Photo CopyReference(bool clearCrop=false,bool attach=true)
    {
        var copy=new Photo { AssetId=AssetId,OriginalPath=OriginalPath,Path=Path,CapturedAt=CapturedAt,PreviewPath=PreviewPath,ThumbnailPath=ThumbnailPath,Error=Error,Excluded=Excluded,Crop=clearCrop?null:Crop };
        if(attach && Asset is not null) copy.Attach(Asset); return copy;
    }
}

public sealed class CarIdentity
{
    public string Brand { get; set; } = "";
    public string Series { get; set; } = "";
    public string Variant { get; set; } = "";
    public string Body { get; set; } = "";
    public string OfficialName { get; set; } = "";
    public string Generation { get; set; } = "";
    public string Code { get; set; } = "";
    public string Phase { get; set; } = "";
    public string Package { get; set; } = "";
    public string Key => string.Join("|", new[] { Brand, Series, Variant, Body, Generation, Code, Phase, Package }.Select(s => s.Trim().ToLowerInvariant()));
    public CarIdentity Clone() => (CarIdentity)MemberwiseClone();
}

public sealed record Citation(string Title, string Url, string Claim);
public sealed class RecognitionResult
{
    public CarIdentity Identity { get; set; } = new();
    public string SuggestedName { get; set; } = "";
    public string Observations { get; set; } = "";
    public string Research { get; set; } = "";
    public string Uncertainties { get; set; } = "";
    public List<string> Alternatives { get; set; } = [];
    public List<string> Queries { get; set; } = [];
    public List<Citation> Sources { get; set; } = [];
    public bool Conflict { get; set; }
    public bool Grounded => Queries.Count > 0 && Sources.Count > 0 && !Conflict;
    public string Model { get; set; } = "";
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long? ThinkingTokens { get; set; }
    public int? SearchCount { get; set; }
    public string ActualSearch { get; set; } = "未知";
    public string RawUsage { get; set; } = "";
    public string SearchSuggestionsHtml { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class PhotoGroup : Observable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ObservableCollection<Photo> Photos { get; set; } = [];
    public string RepresentativeId { get; set; } = "";
    public string LastViewedId { get; set; } = "";
    public string PreviousFolderName { get; set; } = "";
    public bool UserTouched { get; set; }
    public string ImportBatchId { get; set; } = "";
    public long Revision { get; set; }
    public string LastError { get; set; } = "";
    public string TaskKind { get; set; } = "";
    private GroupTaskState taskState;
    public GroupTaskState TaskState { get=>taskState; set { if(Set(ref taskState,value)) Refresh(); } }
    [JsonIgnore] public bool IsWorking => TaskState is GroupTaskState.Queued or GroupTaskState.Running;
    [JsonIgnore] public bool Editable => !IsWorking && !Archived;
    [JsonIgnore] public string TaskColor => Archived?"#00000000":TaskState switch { GroupTaskState.Running=>"#DF4050",GroupTaskState.Completed=>"#23A46B",GroupTaskState.Failed or GroupTaskState.Canceled=>"#D89920",_=>"#929AA5" };
    [JsonIgnore] public double DisplayOpacity => Archived?0.52:1;
    [JsonIgnore] public double ArchivedStrikeOpacity => Archived?0.8:0;
    [JsonIgnore] public string TaskLabel => TaskState switch { GroupTaskState.Queued=>TaskKind+" · 排队",GroupTaskState.Running=>TaskKind+" · 进行中",GroupTaskState.Completed=>TaskKind+" · 已完成",GroupTaskState.Failed=>TaskKind+" · "+LastError,GroupTaskState.Canceled=>TaskKind+" · 已取消",_=>State };
    public string State { get; set; } = "待复核分组";
    public RecognitionResult? Suggestion { get; set; }
    public CarIdentity Confirmed { get; set; } = new();
    public string FolderName { get; set; } = "";
    public string TargetBrand { get; set; } = "";
    public string AppliedRule { get; set; } = "";
    public string LastGeneratedName { get; set; } = "";
    public bool ManualName { get; set; }
    public bool Archived { get; set; }
    public bool GroupConfirmed { get; set; }
    [JsonIgnore] public string Title => FolderName.Length > 0 ? FolderName : Photos.FirstOrDefault()?.Name ?? "空组";
    [JsonIgnore] public string Subtitle => $"{Photos.Count(p => !p.Excluded)} 张 · {TaskLabel}";
    [JsonIgnore] public string? CoverUri => Photos.FirstOrDefault(p => p.Id == RepresentativeId)?.ThumbnailUri ?? Photos.FirstOrDefault()?.ThumbnailUri;
    public void Refresh() { foreach(var name in new[]{nameof(Title),nameof(Subtitle),nameof(CoverUri),nameof(IsWorking),nameof(Editable),nameof(TaskColor),nameof(DisplayOpacity),nameof(ArchivedStrikeOpacity),nameof(TaskLabel)}) Notify(name); }
}

public sealed class NamingRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Brand { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Description => $"{(Enabled ? "启用" : "停用")} · {(Brand.Length == 0 ? "所有品牌" : Brand)} · {From} → {To}";
}
public sealed record LearnedName(string IdentityKey, string Brand, string FolderName);
public sealed class AppSettings
{
    public string ArchiveRoot { get; set; } = "";
    public string Model { get; set; } = "gemini-3.8-flash";
    public RecognitionMode Mode { get; set; } = RecognitionMode.FieldsOnly;
    public SearchPreference Search { get; set; } = SearchPreference.Automatic;
    public string ImageSearchVerifiedModel { get; set; } = "";
    public LearningMode Learning { get; set; } = LearningMode.ConfirmRules;
    public int BatchCallLimit { get; set; } = 100;
    public int GroupGapSeconds { get; set; } = 90;
}
public sealed class WorkspaceState
{
    public int SchemaVersion { get; set; }
    public List<ImageAsset> Assets { get; set; } = [];
    public List<PhotoGroup> Groups { get; set; } = [];
    public AppSettings Settings { get; set; } = new();
    public List<NamingRule> Rules { get; set; } = [];
    public List<LearnedName> Names { get; set; } = [];
    public Dictionary<string,string> BrandFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int BatchCalls { get; set; }
    public string BatchId { get; set; } = Guid.NewGuid().ToString("N");
}
public sealed class MoveEntry
{
    public string AssetId { get; set; } = "";
    public bool RetainSource { get; set; }
    public bool RestoreExisting { get; set; }
    public string PhotoId { get; set; } = "";
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public string Temp { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Stage { get; set; } = "Planned";
    public string Error { get; set; } = "";
}
public sealed class ArchiveJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string GroupId { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string Direction { get; set; } = "Archive";
    public string ParentJobId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public List<MoveEntry> Entries { get; set; } = [];
}

public interface IStateStore
{
    WorkspaceState Load();
    void Save(WorkspaceState state);
    void SaveJob(ArchiveJob job);
    List<ArchiveJob> Jobs();
    void SaveGroup(PhotoGroup group) { }
    void SaveAsset(ImageAsset asset) { }
}
public interface IPhotoService
{
    Task<Photo> ImportAsync(string path, CancellationToken ct);
    Task<byte[]> AnalysisJpegAsync(Photo photo, int longestEdge, CancellationToken ct);
}
public interface IRecognitionService
{
    Task<RecognitionResult> RecognizeAsync(IReadOnlyList<Photo> photos, AppSettings settings, bool verifyAgain, CancellationToken ct);
    Task<List<List<string>>> GroupAsync(IReadOnlyList<Photo> photos, AppSettings settings, CancellationToken ct);
}
