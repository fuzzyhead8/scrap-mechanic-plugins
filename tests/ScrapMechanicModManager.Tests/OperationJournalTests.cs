using System.Text;
using ScrapMechanicModManager.Core.History;
using ScrapMechanicModManager.Core.Localization;
using ScrapMechanicModManager.Core.Settings;

namespace ScrapMechanicModManager.Tests;

public sealed class OperationJournalTests : IDisposable
{
    private readonly string _temporaryRoot = Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public void Missing_journal_returns_an_empty_history()
    {
        var journal = new JsonLinesOperationJournal(JournalPath);

        IReadOnlyList<OperationRecord> records = journal.ReadRecent();

        Assert.Empty(records);
    }

    [Fact]
    public void Operation_record_round_trips_as_one_utf8_json_line()
    {
        DateTimeOffset timestamp = new(2026, 8, 23, 12, 25, 50, TimeSpan.Zero);
        string backupPath = Path.Combine(_temporaryRoot, "backups", "árvíztűrő-snapshot");
        var expected = new OperationRecord
        {
            TimestampUtc = timestamp,
            Severity = OperationSeverity.Information,
            MessageKey = TextKey.LogBackupDirectory.ToString(),
            Arguments = [backupPath],
            ModuleIds =
            [
                BuiltInModuleIds.RobotLoot,
                BuiltInModuleIds.BeehiveAutomation,
            ],
            OperationId = "install-123",
            BackupDirectory = backupPath,
            TechnicalErrorType = null,
            TechnicalDetail = null,
            FallbackText = $"Backup directory: {backupPath}",
        };
        var journal = new JsonLinesOperationJournal(JournalPath);

        bool appended = journal.TryAppend(expected, out string? error);
        OperationRecord actual = Assert.Single(journal.ReadRecent());

        Assert.True(appended);
        Assert.Null(error);
        Assert.Equal(1, actual.SchemaVersion);
        Assert.Equal(expected.TimestampUtc, actual.TimestampUtc);
        Assert.Equal(expected.Severity, actual.Severity);
        Assert.Equal(expected.MessageKey, actual.MessageKey);
        Assert.Equal(expected.Arguments, actual.Arguments);
        Assert.Equal(expected.ModuleIds, actual.ModuleIds);
        Assert.Equal(expected.OperationId, actual.OperationId);
        Assert.Equal(expected.BackupDirectory, actual.BackupDirectory);
        Assert.Equal(expected.TechnicalErrorType, actual.TechnicalErrorType);
        Assert.Equal(expected.TechnicalDetail, actual.TechnicalDetail);
        Assert.Equal(expected.FallbackText, actual.FallbackText);

        byte[] bytes = File.ReadAllBytes(JournalPath);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Single(File.ReadAllLines(JournalPath));
    }

    [Fact]
    public void Append_creates_the_parent_directory()
    {
        var journal = new JsonLinesOperationJournal(JournalPath);

        bool appended = journal.TryAppend(CreateRecord(), out string? error);

        Assert.True(appended);
        Assert.Null(error);
        Assert.True(File.Exists(JournalPath));
    }

    private string JournalPath => Path.Combine(
        _temporaryRoot,
        "nested",
        "logs",
        "operations.jsonl");

    private static OperationRecord CreateRecord() => new()
    {
        TimestampUtc = DateTimeOffset.UtcNow,
        Severity = OperationSeverity.Information,
        MessageKey = TextKey.LogLatestRelease.ToString(),
        Arguments = ["v0.2.0-preview.6"],
        FallbackText = "Latest release: v0.2.0-preview.6",
    };

    public void Dispose()
    {
        Directory.Delete(_temporaryRoot, recursive: true);
    }
}
