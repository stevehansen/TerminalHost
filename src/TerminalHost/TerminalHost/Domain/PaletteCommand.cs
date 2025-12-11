namespace TerminalHost.Domain;

public class PaletteCommand
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Shortcut { get; init; }
    public string? Icon { get; init; }
    public string Category { get; init; } = "General";
    public required Action Execute { get; init; }
    public Func<bool>? CanExecute { get; init; }

    public string DisplayName => string.IsNullOrEmpty(Icon) ? Name : $"{Icon}  {Name}";
    public string DisplayShortcut => Shortcut ?? "";
}
