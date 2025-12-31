namespace TerminalHost.Core.Domain;

/// <summary>
/// File picker filter definition.
/// </summary>
/// <param name="Name">Display name for the filter (e.g., "Text Files").</param>
/// <param name="Extensions">File extensions to filter (e.g., ".txt", ".md").</param>
public record FilePickerFilter(string Name, params string[] Extensions);
