namespace ScrapMechanicModManager.Core.Localization;

public sealed class LocalizedMessage
{
    private readonly object?[] _arguments;

    public LocalizedMessage(TextKey key, params object?[] arguments)
    {
        Key = key;
        _arguments = arguments is null
            ? []
            : (object?[])arguments.Clone();
        Arguments = Array.AsReadOnly(_arguments);
    }

    public TextKey Key { get; }

    public IReadOnlyList<object?> Arguments { get; }

    public static LocalizedMessage FromPersisted(
        string? messageKey,
        IReadOnlyList<string>? arguments,
        string? fallbackText)
    {
        if (!string.IsNullOrWhiteSpace(messageKey)
            && !char.IsDigit(messageKey[0])
            && Enum.TryParse(messageKey, ignoreCase: false, out TextKey key)
            && Enum.IsDefined(key))
        {
            return new LocalizedMessage(
                key,
                arguments?.Cast<object?>().ToArray() ?? []);
        }

        string fallback = !string.IsNullOrWhiteSpace(fallbackText)
            ? fallbackText
            : !string.IsNullOrWhiteSpace(messageKey)
                ? messageKey
                : "Unknown persisted event";
        return new LocalizedMessage(TextKey.LogUnknownPersistedEvent, fallback);
    }

    public string Render(AppLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return localizer.Get(Key, _arguments);
    }
}
