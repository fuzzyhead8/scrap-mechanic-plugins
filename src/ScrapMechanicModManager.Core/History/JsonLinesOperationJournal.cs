using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScrapMechanicModManager.Core.History;

public sealed class JsonLinesOperationJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

    private readonly string _journalPath;
    private readonly OperationJournalOptions _options;
    private readonly object _sync = new();

    public JsonLinesOperationJournal(
        string journalPath,
        OperationJournalOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath);
        _journalPath = Path.GetFullPath(journalPath);
        _options = options ?? OperationJournalOptions.Default;
        if (_options.MaxAge < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The maximum journal age cannot be negative.");
        }
        if (_options.MaxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The maximum journal size must be positive.");
        }
        if (_options.MaxUiEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The maximum UI entry count must be positive.");
        }
    }

    public bool TryAppend(OperationRecord record, out string? error)
    {
        ArgumentNullException.ThrowIfNull(record);

        try
        {
            lock (_sync)
            {
                string directory = RequireParentDirectory();
                Directory.CreateDirectory(directory);
                string json = JsonSerializer.Serialize(record, JsonOptions);
                long lineSize = Utf8WithoutBom.GetByteCount(json)
                    + Utf8WithoutBom.GetByteCount(Environment.NewLine);
                if (lineSize > _options.MaxBytes)
                {
                    throw new InvalidDataException(
                        "The operation record exceeds the journal size limit.");
                }

                using (var stream = new FileStream(
                           _journalPath,
                           FileMode.Append,
                           FileAccess.Write,
                           FileShare.Read))
                using (var writer = new StreamWriter(stream, Utf8WithoutBom))
                {
                    writer.WriteLine(json);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                if (new FileInfo(_journalPath).Length > _options.MaxBytes)
                {
                    CompactUnsafe(
                        DateTimeOffset.UtcNow,
                        out string? compactionError);
                    if (compactionError is not null)
                    {
                        error = compactionError;
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }
        catch (Exception exception) when (IsRecoverableFileError(exception))
        {
            error = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    public IReadOnlyList<OperationRecord> ReadRecent() =>
        ReadRecent(DateTimeOffset.UtcNow);

    public IReadOnlyList<OperationRecord> ReadRecent(DateTimeOffset nowUtc)
    {
        TryReadRecent(out IReadOnlyList<OperationRecord> records, out _, nowUtc);
        return records;
    }

    public bool TryReadRecent(
        out IReadOnlyList<OperationRecord> records,
        out string? error,
        DateTimeOffset? nowUtc = null)
    {
        try
        {
            lock (_sync)
            {
                if (Directory.Exists(_journalPath))
                {
                    throw new IOException(
                        "The operation journal path points to a directory.");
                }
                if (!File.Exists(_journalPath))
                {
                    records = [];
                    error = null;
                    return true;
                }

                IReadOnlyList<OperationRecord> retained = CompactUnsafe(
                    nowUtc ?? DateTimeOffset.UtcNow,
                    out error);
                records = retained.TakeLast(_options.MaxUiEntries).ToArray();
                return error is null;
            }
        }
        catch (Exception exception) when (IsRecoverableFileError(exception))
        {
            records = [];
            error = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private IReadOnlyList<OperationRecord> CompactUnsafe(
        DateTimeOffset nowUtc,
        out string? error)
    {
        List<OperationRecord> parsed = ReadValidRecordsUnsafe(out bool skippedInvalidLine);
        DateTimeOffset oldestAllowed = nowUtc - _options.MaxAge;
        OperationRecord[] withinAge = parsed
            .Where(record => record.TimestampUtc >= oldestAllowed)
            .ToArray();
        IReadOnlyList<OperationRecord> retained = KeepNewestWithinSize(withinAge);
        bool needsRewrite = skippedInvalidLine
            || retained.Count != parsed.Count
            || new FileInfo(_journalPath).Length > _options.MaxBytes;
        if (needsRewrite)
        {
            try
            {
                RewriteUnsafe(retained);
            }
            catch (Exception exception) when (IsRecoverableFileError(exception))
            {
                error = $"{exception.GetType().Name}: {exception.Message}";
                return retained;
            }
        }
        error = null;
        return retained;
    }

    private List<OperationRecord> ReadValidRecordsUnsafe(out bool skippedInvalidLine)
    {
        var records = new List<OperationRecord>();
        skippedInvalidLine = false;
        foreach (string line in File.ReadLines(_journalPath, Utf8WithoutBom))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                skippedInvalidLine = true;
                continue;
            }

            try
            {
                OperationRecord? record = JsonSerializer.Deserialize<OperationRecord>(
                    line,
                    JsonOptions);
                if (record is null)
                {
                    skippedInvalidLine = true;
                    continue;
                }
                records.Add(record);
            }
            catch (JsonException)
            {
                skippedInvalidLine = true;
            }
        }
        return records;
    }

    private IReadOnlyList<OperationRecord> KeepNewestWithinSize(
        IReadOnlyList<OperationRecord> records)
    {
        var newestFirst = new List<OperationRecord>();
        long retainedBytes = 0;
        long newlineBytes = Utf8WithoutBom.GetByteCount(Environment.NewLine);
        for (int index = records.Count - 1; index >= 0; index--)
        {
            string json = JsonSerializer.Serialize(records[index], JsonOptions);
            long recordBytes = Utf8WithoutBom.GetByteCount(json) + newlineBytes;
            if (retainedBytes + recordBytes > _options.MaxBytes)
            {
                break;
            }
            newestFirst.Add(records[index]);
            retainedBytes += recordBytes;
        }
        newestFirst.Reverse();
        return newestFirst;
    }

    private void RewriteUnsafe(IReadOnlyList<OperationRecord> records)
    {
        string directory = RequireParentDirectory();
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $"{Path.GetFileName(_journalPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new StreamWriter(stream, Utf8WithoutBom))
            {
                foreach (OperationRecord record in records)
                {
                    writer.WriteLine(JsonSerializer.Serialize(record, JsonOptions));
                }
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _journalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private string RequireParentDirectory() =>
        Path.GetDirectoryName(_journalPath)
        ?? throw new InvalidOperationException(
            "The operation journal path has no parent directory.");

    private static bool IsRecoverableFileError(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or InvalidDataException
            or NotSupportedException;
}
