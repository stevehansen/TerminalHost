using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.ViewModels;

/// <summary>
/// ViewModel for a Claude Code session block in the timeline.
/// </summary>
public partial class SessionBlockViewModel : ObservableObject
{
    private readonly IntentRowViewModel _parent;
    private readonly ClaudeSession _session;

    public string Id => _session.Id;
    public string IntentId => _session.IntentId;
    public ClaudeSessionStatus Status => _session.Status;
    public DateTime StartTime => _session.StartTime;
    public DateTime? EndTime => _session.EndTime;
    public string? CommitHash => _session.CommitHash;
    public string? CommitMessage => _session.CommitMessage;
    public string? AgentNotes => _session.AgentNotes;
    public string? InitialPrompt => _session.InitialPrompt;
    public bool IsFork => !string.IsNullOrEmpty(_session.ParentSessionId);

    public string StatusIcon => _session.StatusIcon;
    public string StatusColorHex => _session.StatusColorHex;
    public string DurationDisplay => _session.DurationDisplay;

    public string ShortCommitHash => CommitHash?.Length > 7 ? CommitHash[..7] : CommitHash ?? "";

    public string TimeRangeDisplay
    {
        get
        {
            var start = StartTime.ToLocalTime().ToString("HH:mm");
            var end = EndTime?.ToLocalTime().ToString("HH:mm") ?? "...";
            return $"{start} → {end}";
        }
    }

    public string StatusText => Status switch
    {
        ClaudeSessionStatus.Running => "RUNNING",
        ClaudeSessionStatus.Success => "SUCCESS",
        ClaudeSessionStatus.Failed => "FAILED",
        ClaudeSessionStatus.Abandoned => "ABANDONED",
        _ => "UNKNOWN"
    };

    public bool HasCommit => !string.IsNullOrEmpty(CommitHash);
    public bool HasNotes => !string.IsNullOrEmpty(AgentNotes);
    public bool HasFilesChanged => FilesChanged.Any();
    public bool HasCommands => Commands.Any();
    public bool IsRunning => Status == ClaudeSessionStatus.Running;
    public bool IsCompleted => Status != ClaudeSessionStatus.Running;
    public bool IsAbandoned => Status == ClaudeSessionStatus.Abandoned;

    /// <summary>
    /// Number of files changed in this session.
    /// </summary>
    public int FileCount => _session.FilesChanged.Count;

    /// <summary>
    /// Display text for the session block - shows commit message, agent notes, or default.
    /// </summary>
    public string BlockTitle
    {
        get
        {
            if (IsRunning)
                return "Claude Code working...";

            // Try commit message first (most authoritative)
            if (!string.IsNullOrEmpty(CommitMessage))
                return TruncateTitle(CommitMessage);

            // Then try agent notes/summary
            if (!string.IsNullOrEmpty(AgentNotes))
                return TruncateTitle(AgentNotes);

            // Then try initial prompt
            if (!string.IsNullOrEmpty(InitialPrompt))
                return TruncateTitle(InitialPrompt);

            return "Claude Code";
        }
    }

    /// <summary>
    /// Truncates a title to a reasonable display length.
    /// </summary>
    private static string TruncateTitle(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Clean up the text (remove newlines, collapse whitespace)
        text = text.Replace('\r', ' ').Replace('\n', ' ');
        while (text.Contains("  "))
            text = text.Replace("  ", " ");
        text = text.Trim();

        const int maxLength = 50;
        if (text.Length <= maxLength)
            return text;

        // Find a good break point
        var breakPoint = text.LastIndexOf(' ', maxLength);
        if (breakPoint < maxLength / 2)
            breakPoint = maxLength;

        return text[..breakPoint].TrimEnd() + "...";
    }

    /// <summary>
    /// Compact summary for the session block (e.g., "25m • 3 files")
    /// </summary>
    public string BlockSummary
    {
        get
        {
            var parts = new List<string>();
            parts.Add(DurationDisplay);
            if (FileCount > 0)
                parts.Add($"{FileCount} files");
            return string.Join(" • ", parts);
        }
    }

    // Timeline positioning properties
    [ObservableProperty]
    private double _xPosition;

    [ObservableProperty]
    private double _width = 120; // Minimum width

    [ObservableProperty]
    private double _progressWidth; // For running sessions, the progress bar width

    /// <summary>
    /// Update the position based on timeline view parameters.
    /// </summary>
    public void UpdatePosition(DateTime viewStartTime, double pixelsPerMinute)
    {
        // Calculate X position from start time (convert to local for comparison)
        var localStart = StartTime.ToLocalTime();
        var minutesFromStart = (localStart - viewStartTime).TotalMinutes;
        XPosition = Math.Max(0, minutesFromStart * pixelsPerMinute);

        // Calculate width from duration
        var endTimeActual = EndTime?.ToLocalTime() ?? DateTime.Now;
        var durationMinutes = (endTimeActual - localStart).TotalMinutes;
        var calculatedWidth = durationMinutes * pixelsPerMinute;

        // Minimum width for readability
        Width = Math.Max(120, calculatedWidth);

        // For running sessions, show progress within the block
        if (IsRunning)
        {
            ProgressWidth = calculatedWidth;
        }
    }

    [ObservableProperty]
    private ObservableCollection<FileChangeViewModel> _filesChanged = [];

    [ObservableProperty]
    private ObservableCollection<string> _commands = [];

    public SessionBlockViewModel(ClaudeSession session, IntentRowViewModel parent)
    {
        _session = session;
        _parent = parent;

        // Load file changes with relative paths based on worktree
        var basePath = parent.WorktreePath;
        foreach (var change in session.FilesChanged)
        {
            FilesChanged.Add(new FileChangeViewModel(change, basePath));
        }

        // Load commands
        foreach (var cmd in session.CommandsExecuted)
        {
            Commands.Add(cmd);
        }
    }

    [RelayCommand]
    private void Select()
    {
        _parent.SelectSession(this);
    }

    [RelayCommand]
    private async Task Fork()
    {
        await _parent.ForkSession(Id);
    }

    [RelayCommand]
    private async Task CherryPick()
    {
        await _parent.CherryPickSession(Id);
    }

    [RelayCommand]
    private void MarkSuccess()
    {
        _parent.MarkSessionSuccess(Id);
    }

    [RelayCommand]
    private void MarkFailed()
    {
        _parent.MarkSessionFailed(Id);
    }

    [RelayCommand]
    private void MarkAbandoned()
    {
        _parent.MarkSessionAbandoned(Id);
    }
}

/// <summary>
/// ViewModel for a file change in a session.
/// </summary>
public class FileChangeViewModel
{
    public string Path { get; }
    public string RelativePath { get; }
    public string FileName { get; }
    public int Additions { get; }
    public int Deletions { get; }
    public string Summary { get; }
    public bool HasLineChanges { get; }

    public FileChangeViewModel(FileChange change, string? basePath = null)
    {
        Path = change.Path;
        FileName = System.IO.Path.GetFileName(change.Path);
        Additions = change.Additions;
        Deletions = change.Deletions;
        Summary = change.ChangeSummary;
        HasLineChanges = Additions > 0 || Deletions > 0;

        // Make path relative if possible
        if (!string.IsNullOrEmpty(basePath) && change.Path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
        {
            RelativePath = change.Path.Substring(basePath.Length).TrimStart('\\', '/');
        }
        else
        {
            // Try to extract a reasonable relative path from the full path
            // Look for common patterns like "src/", "plugins/", etc.
            var path = change.Path.Replace('\\', '/');
            var markers = new[] { "/src/", "/plugins/", "/tests/", "/docs/" };
            foreach (var marker in markers)
            {
                var idx = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    RelativePath = path.Substring(idx + 1); // Keep "src/..." part
                    return;
                }
            }
            // Fallback: just use filename
            RelativePath = FileName;
        }
    }
}
