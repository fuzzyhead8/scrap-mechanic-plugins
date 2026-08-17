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

    public string Render(AppLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return localizer.Get(Key, _arguments);
    }
}
