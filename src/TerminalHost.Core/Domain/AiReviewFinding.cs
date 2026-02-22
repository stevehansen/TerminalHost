namespace TerminalHost.Core.Domain;

public class AiReviewFinding
{
    public string Emoji { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Location { get; set; }
    public string Description { get; set; } = "";

    public bool IsError => Emoji == "🔴";
    public bool IsWarning => Emoji == "🟡";
    public bool IsGood => Emoji == "🟢";
    public bool HasLocation => !string.IsNullOrEmpty(Location);
}
