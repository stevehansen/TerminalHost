using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TerminalHost.Domain;

/// <summary>
/// Represents search results for a single file.
/// </summary>
public partial class SearchResult : ObservableObject
{
    /// <summary>
    /// The full path to the file.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// The path relative to the search directory.
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// The list of matches found in this file.
    /// </summary>
    public List<SearchMatch> Matches { get; set; } = [];

    /// <summary>
    /// Whether this result is expanded in the tree view.
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>
    /// Gets the file name without path.
    /// </summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>
    /// Gets the file extension.
    /// </summary>
    public string Extension => Path.GetExtension(FilePath);

    /// <summary>
    /// Gets the directory containing the file.
    /// </summary>
    public string Directory => Path.GetDirectoryName(RelativePath) ?? string.Empty;

    /// <summary>
    /// Gets the icon for this file type.
    /// </summary>
    public string Icon => FileIconMapper.GetIcon(FileName, isDirectory: false);

    /// <summary>
    /// Gets the total number of matches in this file.
    /// </summary>
    public int MatchCount => Matches.Count;

    /// <summary>
    /// Gets a display summary of matches.
    /// </summary>
    public string MatchSummary => MatchCount == 1 ? "1 match" : $"{MatchCount} matches";
}
