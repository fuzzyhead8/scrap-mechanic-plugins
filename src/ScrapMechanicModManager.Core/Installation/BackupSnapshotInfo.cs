namespace ScrapMechanicModManager.Core.Installation;

public sealed record BackupSnapshotInfo(
    string SnapshotDirectory,
    BackupSnapshotState State,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<string> ModuleIds,
    IReadOnlyDictionary<string, string> ModuleVersions,
    string? Error);
