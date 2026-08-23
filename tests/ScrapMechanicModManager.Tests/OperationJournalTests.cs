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
        string backupPath = Path.Combine(
            _temporaryRoot,
            "backups",
            "\u00E1rv\u00EDzt\u0171r\u0151-snapshot");
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

    [Fact]
    public void Malformed_and_partial_lines_are_skipped_and_compacted()
    {
        var journal = new JsonLinesOperationJournal(JournalPath);
        Assert.True(journal.TryAppend(CreateRecord("first"), out _));
        Assert.True(journal.TryAppend(CreateRecord("second"), out _));
        string[] validLines = File.ReadAllLines(JournalPath);
        File.WriteAllText(
            JournalPath,
            $"{validLines[0]}{Environment.NewLine}not-json{Environment.NewLine}" +
            $"{validLines[1]}{Environment.NewLine}{{\"partial\"");

        IReadOnlyList<OperationRecord> records = journal.ReadRecent();

        Assert.Equal(["first", "second"], records.Select(record => record.OperationId));
        Assert.Equal(2, File.ReadAllLines(JournalPath).Length);
        Assert.Empty(Directory.EnumerateFiles(
            Path.GetDirectoryName(JournalPath)!,
            "*.tmp"));
    }

    [Fact]
    public void Unknown_message_key_survives_with_its_fallback_text()
    {
        var journal = new JsonLinesOperationJournal(JournalPath);
        OperationRecord expected = CreateRecord("future") with
        {
            MessageKey = "FutureMessageKey",
            FallbackText = "A future event",
        };
        Assert.True(journal.TryAppend(expected, out _));

        OperationRecord actual = Assert.Single(journal.ReadRecent());

        Assert.Equal("FutureMessageKey", actual.MessageKey);
        Assert.Equal("A future event", actual.FallbackText);
    }

    [Fact]
    public void Records_older_than_the_retention_window_are_removed()
    {
        DateTimeOffset now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        var options = new OperationJournalOptions(
            TimeSpan.FromDays(90),
            10 * 1024 * 1024,
            200);
        var journal = new JsonLinesOperationJournal(JournalPath, options);
        Assert.True(journal.TryAppend(CreateRecord("old") with
        {
            TimestampUtc = now.AddDays(-91),
        }, out _));
        Assert.True(journal.TryAppend(CreateRecord("recent") with
        {
            TimestampUtc = now.AddDays(-89),
        }, out _));

        IReadOnlyList<OperationRecord> records = journal.ReadRecent(now);

        Assert.Equal("recent", Assert.Single(records).OperationId);
        Assert.Single(File.ReadAllLines(JournalPath));
    }

    [Fact]
    public void Failed_compaction_preserves_the_original_and_returns_readable_records()
    {
        DateTimeOffset now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        var options = new OperationJournalOptions(
            TimeSpan.FromDays(90),
            10 * 1024 * 1024,
            200);
        var journal = new JsonLinesOperationJournal(JournalPath, options);
        Assert.True(journal.TryAppend(CreateRecord("old") with
        {
            TimestampUtc = now.AddDays(-91),
        }, out _));
        Assert.True(journal.TryAppend(CreateRecord("recent") with
        {
            TimestampUtc = now.AddDays(-1),
        }, out _));
        byte[] original = File.ReadAllBytes(JournalPath);

        bool read;
        IReadOnlyList<OperationRecord> records;
        string? error;
        if (OperatingSystem.IsWindows())
        {
            using var heldOpen = new FileStream(
                JournalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            read = journal.TryReadRecent(out records, out error, now);
        }
        else
        {
            string directory = Path.GetDirectoryName(JournalPath)!;
            UnixFileMode originalMode = File.GetUnixFileMode(directory);
            try
            {
                File.SetUnixFileMode(
                    directory,
                    UnixFileMode.UserRead | UnixFileMode.UserExecute);
                read = journal.TryReadRecent(out records, out error, now);
            }
            finally
            {
                File.SetUnixFileMode(directory, originalMode);
            }
        }

        Assert.False(read);
        Assert.Equal("recent", Assert.Single(records).OperationId);
        Assert.Matches(
            "^(IOException|UnauthorizedAccessException):",
            Assert.IsType<string>(error));
        Assert.Equal(original, File.ReadAllBytes(JournalPath));
    }

    [Fact]
    public void Size_compaction_keeps_newest_complete_records_under_the_limit()
    {
        const long maxBytes = 1200;
        var options = new OperationJournalOptions(
            TimeSpan.FromDays(90),
            maxBytes,
            100);
        var journal = new JsonLinesOperationJournal(JournalPath, options);
        for (int index = 0; index < 20; index++)
        {
            OperationRecord record = CreateRecord(index.ToString()) with
            {
                FallbackText = new string('x', 80),
            };
            Assert.True(journal.TryAppend(record, out string? error), error);
        }

        IReadOnlyList<OperationRecord> records = journal.ReadRecent();

        Assert.NotEmpty(records);
        Assert.True(records.Count < 20);
        Assert.Equal("19", records[^1].OperationId);
        Assert.True(new FileInfo(JournalPath).Length <= maxBytes);
        Assert.Empty(Directory.EnumerateFiles(
            Path.GetDirectoryName(JournalPath)!,
            "*.tmp"));
    }

    [Fact]
    public void Read_recent_limits_only_the_UI_result_not_the_valid_journal()
    {
        var options = new OperationJournalOptions(
            TimeSpan.FromDays(90),
            10 * 1024 * 1024,
            2);
        var journal = new JsonLinesOperationJournal(JournalPath, options);
        Assert.True(journal.TryAppend(CreateRecord("one"), out _));
        Assert.True(journal.TryAppend(CreateRecord("two"), out _));
        Assert.True(journal.TryAppend(CreateRecord("three"), out _));

        IReadOnlyList<OperationRecord> records = journal.ReadRecent();

        Assert.Equal(["two", "three"], records.Select(record => record.OperationId));
        Assert.Equal(3, File.ReadAllLines(JournalPath).Length);
    }

    [Fact]
    public void Parallel_appends_produce_complete_parseable_lines()
    {
        var journal = new JsonLinesOperationJournal(JournalPath);

        Parallel.For(0, 50, index =>
        {
            Assert.True(journal.TryAppend(CreateRecord(index.ToString()), out _));
        });

        IReadOnlyList<OperationRecord> records = journal.ReadRecent();
        Assert.Equal(50, records.Count);
        Assert.Equal(50, File.ReadAllLines(JournalPath).Length);
    }

    [Fact]
    public void Unreadable_journal_is_reported_without_throwing()
    {
        Directory.CreateDirectory(JournalPath);
        var journal = new JsonLinesOperationJournal(JournalPath);

        bool read = journal.TryReadRecent(
            out IReadOnlyList<OperationRecord> records,
            out string? error);

        Assert.False(read);
        Assert.Empty(records);
        Assert.Contains("IOException", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Unwritable_parent_is_reported_without_throwing()
    {
        string blocker = Path.Combine(_temporaryRoot, "blocker");
        File.WriteAllText(blocker, "not a directory");
        var journal = new JsonLinesOperationJournal(
            Path.Combine(blocker, "operations.jsonl"));

        bool appended = journal.TryAppend(CreateRecord("blocked"), out string? error);

        Assert.False(appended);
        Assert.Contains("IOException", error, StringComparison.Ordinal);
    }

    private string JournalPath => Path.Combine(
        _temporaryRoot,
        "nested",
        "logs",
        "operations.jsonl");

    private static OperationRecord CreateRecord(string? operationId = null) => new()
    {
        TimestampUtc = DateTimeOffset.UtcNow,
        Severity = OperationSeverity.Information,
        MessageKey = TextKey.LogLatestRelease.ToString(),
        Arguments = ["v0.2.0-preview.6"],
        OperationId = operationId,
        FallbackText = "Latest release: v0.2.0-preview.6",
    };

    public void Dispose()
    {
        Directory.Delete(_temporaryRoot, recursive: true);
    }
}
