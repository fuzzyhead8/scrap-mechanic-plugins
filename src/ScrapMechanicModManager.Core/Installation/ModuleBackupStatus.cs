namespace ScrapMechanicModManager.Core.Installation;

public sealed record ModuleBackupStatus(
    string ModId,
    BackupSnapshotState State,
    string? SnapshotDirectory,
    DateTimeOffset? CreatedAtUtc,
    string? Version);
