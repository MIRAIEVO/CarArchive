namespace CarArchive.Core;

public sealed record ArchiveDuplicate(string Source,string Existing,string Hash);
public enum ArchiveDuplicateChoice { Move, Recycle, Cancel }
