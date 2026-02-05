namespace TerminalHost.Core.Domain;

public class GitTag
{
    public string Name { get; set; } = "";
    public string Hash { get; set; } = "";
    public string ShortHash { get; set; } = "";
    public string? Message { get; set; }
    public bool IsAnnotated { get; set; }
    public string? TaggerName { get; set; }
    public string? TaggerDate { get; set; }
    public string? CommitSubject { get; set; }
}
