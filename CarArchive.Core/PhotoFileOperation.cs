namespace CarArchive.Core;

public sealed class PhotoFileOperation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "Copy";
    public string GroupId { get; set; } = "";
    public string SourceGroupId { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<PhotoFileEntry> Entries { get; set; } = [];
}
public sealed class PhotoFileEntry
{
    public List<string> PhotoIds { get; set; } = [];
    public string SourceAssetId { get; set; } = "";
    public string NewAssetId { get; set; } = Guid.NewGuid().ToString("N");
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public string Temporary { get; set; } = "";
    public string Hash { get; set; } = "";
    public long Length { get; set; }
    public long ModifiedTicks { get; set; }
    public long CreatedTicks { get; set; }
    public string Stage { get; set; } = "Planned";
    public string Error { get; set; } = "";
    public string RecycleIdentity { get; set; } = "";
    public long CopiedModifiedTicks { get; set; }
}
public interface IPhotoOperationStore
{
    void SavePhotoOperation(PhotoFileOperation operation);
    List<PhotoFileOperation> PhotoOperations();
}
public interface IPhotoRecycler
{
    Task<string> RecycleAsync(PhotoFileEntry entry, CancellationToken cancellationToken);
}

/// <summary>The recycler rejected an operation before any source deletion was dispatched.</summary>
public sealed class PhotoRecycleRejectedException(string message) : IOException(message);
