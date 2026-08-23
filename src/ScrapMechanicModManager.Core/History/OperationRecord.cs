namespace ScrapMechanicModManager.Core.History;

public sealed record OperationRecord
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset TimestampUtc { get; init; }
    public OperationSeverity Severity { get; init; }
    public string MessageKey { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public IReadOnlyList<string> ModuleIds { get; init; } = [];
    public string? OperationId { get; init; }
    public string? BackupDirectory { get; init; }
    public string? TechnicalErrorType { get; init; }
    public string? TechnicalDetail { get; init; }
    public string? FallbackText { get; init; }
}
