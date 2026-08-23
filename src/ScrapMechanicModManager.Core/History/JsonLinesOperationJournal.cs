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
    }

    public bool TryAppend(OperationRecord record, out string? error)
    {
        ArgumentNullException.ThrowIfNull(record);

        try
        {
            lock (_sync)
            {
                string? directory = Path.GetDirectoryName(_journalPath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    throw new InvalidOperationException(
                        "The operation journal path has no parent directory.");
                }

                Directory.CreateDirectory(directory);
                string json = JsonSerializer.Serialize(record, JsonOptions);
                using var stream = new FileStream(
                    _journalPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read);
                using var writer = new StreamWriter(stream, Utf8WithoutBom);
                writer.WriteLine(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            error = null;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or NotSupportedException)
        {
            error = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    public IReadOnlyList<OperationRecord> ReadRecent()
    {
        lock (_sync)
        {
            if (!File.Exists(_journalPath)) return [];

            return File.ReadLines(_journalPath, Utf8WithoutBom)
                .Select(line => JsonSerializer.Deserialize<OperationRecord>(line, JsonOptions))
                .Where(record => record is not null)
                .Select(record => record!)
                .TakeLast(_options.MaxUiEntries)
                .ToArray();
        }
    }
}
