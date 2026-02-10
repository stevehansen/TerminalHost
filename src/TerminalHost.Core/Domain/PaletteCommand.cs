namespace TerminalHost.Core.Domain;

public class PaletteCommand
{
    public required string Id { get; init; }

    private string _name = "";
    public required string Name
    {
        get => NameProvider?.Invoke() ?? _name;
        init => _name = value;
    }

    private string? _description;
    public string? Description
    {
        get => DescriptionProvider?.Invoke() ?? _description;
        init => _description = value;
    }

    public string? Shortcut { get; init; }
    public string? Icon { get; init; }
    public string Category { get; init; } = "General";
    public DateOnly? IntroducedOn { get; init; }
    public required Action Execute { get; init; }
    public Func<bool>? CanExecute { get; init; }

    /// <summary>
    /// Optional dynamic name provider. When set, overrides the static Name at display/filter time.
    /// </summary>
    public Func<string>? NameProvider { get; init; }

    /// <summary>
    /// Optional dynamic description provider. When set, overrides the static Description at display/filter time.
    /// </summary>
    public Func<string>? DescriptionProvider { get; init; }

    public string DisplayName => string.IsNullOrEmpty(Icon) ? Name : $"{Icon}  {Name}";
    public string DisplayShortcut => Shortcut ?? "";
}
