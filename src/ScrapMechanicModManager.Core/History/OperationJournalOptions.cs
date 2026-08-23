namespace ScrapMechanicModManager.Core.History;

public sealed record OperationJournalOptions(
    TimeSpan MaxAge,
    long MaxBytes,
    int MaxUiEntries)
{
    public static OperationJournalOptions Default { get; } = new(
        TimeSpan.FromDays(90),
        10 * 1024 * 1024,
        200);
}
