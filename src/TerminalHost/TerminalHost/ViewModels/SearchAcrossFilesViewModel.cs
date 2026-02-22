using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for Search Across Files panel (Ctrl+Shift+F).
/// Provides full-text search across all files in a project with filtering,
/// regex support, and replace functionality.
/// </summary>
public partial class SearchAcrossFilesViewModel : BasePanelViewModel
{
    private readonly ISearchService _searchService;
    private readonly IFileSystem _fileSystem;
    private readonly IProcessService _processService;
    private readonly IConfigurationService _configurationService;
    private readonly IToastService _toastService;
    private readonly ITimerService _timerService;
    private readonly IDispatcherService _dispatcherService;

    private TerminalPairTabViewModel? _currentTerminalTab;
    private CancellationTokenSource? _searchCts;
    private IAppTimer? _debounceTimer;

    #region IPanelableViewModel Implementation

    public override string PanelId => "searchFiles";
    public override string PanelTitle => "Search";
    public override string PanelIcon => "🔍";
    public override PanelSizePreset SizePreset => PanelSizePreset.Large;

    #endregion

    #region Search Input Properties

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _searchPattern = "";

    [ObservableProperty]
    private string _replaceText = "";

    [ObservableProperty]
    private bool _caseSensitive;

    [ObservableProperty]
    private bool _wholeWord;

    [ObservableProperty]
    private bool _useRegex;

    [ObservableProperty]
    private string _includePattern = "";

    [ObservableProperty]
    private string _excludePattern = "bin,obj,node_modules,.git";

    [ObservableProperty]
    private bool _useGitignore = true;

    #endregion

    #region State Properties

    [ObservableProperty]
    private string _title = "Search";

    [ObservableProperty]
    private string _workingDirectory = "";

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private int _filesSearched;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _showReplaceSection;

    #endregion

    #region AI Regex Properties

    [ObservableProperty]
    private bool _showRegexInput;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateRegexCommand))]
    private bool _isGeneratingRegex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateRegexCommand))]
    private string _regexDescription = "";

    public bool CanGenerateRegex => !IsGeneratingRegex && !string.IsNullOrWhiteSpace(RegexDescription);

    #endregion

    #region Results Properties

    [ObservableProperty]
    private ObservableCollection<SearchFileResultViewModel> _results = [];

    [ObservableProperty]
    private SearchFileResultViewModel? _selectedFile;

    [ObservableProperty]
    private SearchMatchViewModel? _selectedMatch;

    [ObservableProperty]
    private int _totalMatchCount;

    [ObservableProperty]
    private int _fileCount;

    [ObservableProperty]
    private long _searchTimeMs;

    [ObservableProperty]
    private bool _resultsTruncated;

    #endregion

    #region Events

    /// <summary>
    /// Raised when user wants to open a file at a specific line.
    /// </summary>
    public event EventHandler<OpenFileAtLineEventArgs>? OpenFileAtLineRequested;

    #endregion

    public SearchAcrossFilesViewModel(
        ISearchService searchService,
        IFileSystem fileSystem,
        IProcessService processService,
        IConfigurationService configurationService,
        IToastService toastService,
        ITimerService timerService,
        IDispatcherService dispatcherService)
    {
        _searchService = searchService;
        _fileSystem = fileSystem;
        _processService = processService;
        _configurationService = configurationService;
        _toastService = toastService;
        _timerService = timerService;
        _dispatcherService = dispatcherService;

        // Set defaults for search panel - defaults to Panel
        DisplayState = PanelDisplayState.Panel;
        Width = 900;
        Height = 600;
    }

    #region Property Change Handlers

    partial void OnSearchPatternChanged(string value)
    {
        // Debounced search as user types
        _debounceTimer?.Stop();
        _debounceTimer?.Dispose();
        _debounceTimer = _timerService.CreateTimer(TimeSpan.FromMilliseconds(300), () =>
        {
            _debounceTimer?.Stop();
            _dispatcherService.InvokeAsync(async () =>
            {
                if (!string.IsNullOrEmpty(SearchPattern))
                {
                    await SearchAsync();
                }
            });
        });
        _debounceTimer.Start();
    }

    partial void OnShowRegexInputChanged(bool value)
    {
        if (!value)
            RegexDescription = "";
    }

    partial void OnCaseSensitiveChanged(bool value) => TriggerSearch();
    partial void OnWholeWordChanged(bool value) => TriggerSearch();
    partial void OnUseRegexChanged(bool value) => TriggerSearch();
    partial void OnIncludePatternChanged(string value) => TriggerSearchDelayed();
    partial void OnExcludePatternChanged(string value) => TriggerSearchDelayed();
    partial void OnUseGitignoreChanged(bool value) => TriggerSearch();

    partial void OnSelectedMatchChanged(SearchMatchViewModel? value)
    {
        if (value != null)
        {
            OpenFileAtLineRequested?.Invoke(this, new OpenFileAtLineEventArgs(
                value.FullPath,
                value.LineNumber,
                value.Column));
        }
    }

    private void TriggerSearch()
    {
        if (!string.IsNullOrEmpty(SearchPattern))
        {
            _ = SearchAsync();
        }
    }

    private void TriggerSearchDelayed()
    {
        _debounceTimer?.Stop();
        _debounceTimer?.Dispose();
        _debounceTimer = _timerService.CreateTimer(TimeSpan.FromMilliseconds(500), () =>
        {
            _debounceTimer?.Stop();
            _dispatcherService.InvokeAsync(async () =>
            {
                if (!string.IsNullOrEmpty(SearchPattern))
                {
                    await SearchAsync();
                }
            });
        });
        _debounceTimer.Start();
    }

    #endregion

    #region Overrides

    protected override void OnClose()
    {
        CancelSearch();
        _debounceTimer?.Stop();
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        _currentTerminalTab = null;
        Results.Clear();
        SearchPattern = "";
        ReplaceText = "";
        StatusMessage = "";
        ShowRegexInput = false;
        base.OnClose();
    }

    #endregion

    #region Commands

    [RelayCommand]
    public async Task OpenAsync(TerminalPairTabViewModel terminalTab)
    {
        _currentTerminalTab = terminalTab;
        WorkingDirectory = terminalTab.Pair.WorkingDirectory;
        Title = $"Search - {terminalTab.Title}";

        // Clear previous results
        Results.Clear();
        StatusMessage = "Enter a search term to find matches across files";

        // Request to be shown in the appropriate mode
        RequestShow();

        // If there's already a search pattern, run the search
        if (!string.IsNullOrEmpty(SearchPattern))
        {
            await SearchAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    public async Task SearchAsync()
    {
        if (string.IsNullOrEmpty(SearchPattern) || string.IsNullOrEmpty(WorkingDirectory))
            return;

        // Cancel any existing search
        CancelSearch();

        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsSearching = true;
        FilesSearched = 0;
        StatusMessage = "Searching...";
        Results.Clear();

        try
        {
            var options = new SearchOptions
            {
                CaseSensitive = CaseSensitive,
                WholeWord = WholeWord,
                UseRegex = UseRegex,
                IncludePattern = string.IsNullOrWhiteSpace(IncludePattern) ? null : IncludePattern,
                ExcludePattern = string.IsNullOrWhiteSpace(ExcludePattern) ? null : ExcludePattern,
                UseGitignore = UseGitignore,
                ContextLines = 1,
                MaxResults = 10000
            };

            var searchResults = await _searchService.SearchAsync(
                SearchPattern,
                WorkingDirectory,
                options,
                count => FilesSearched = count,
                token);

            if (token.IsCancellationRequested)
                return;

            // Convert to view models
            var resultVms = searchResults.Files.Select(f => new SearchFileResultViewModel(f, WorkingDirectory)).ToList();
            Results = new ObservableCollection<SearchFileResultViewModel>(resultVms);

            TotalMatchCount = searchResults.TotalMatchCount;
            FileCount = searchResults.FileCount;
            SearchTimeMs = searchResults.SearchTimeMs;
            ResultsTruncated = searchResults.Truncated;

            if (searchResults.TotalMatchCount == 0)
            {
                StatusMessage = $"No results found for \"{SearchPattern}\"";
            }
            else
            {
                var truncatedNote = searchResults.Truncated ? " (results truncated)" : "";
                StatusMessage = $"{searchResults.TotalMatchCount:N0} results in {searchResults.FileCount:N0} files ({searchResults.SearchTimeMs}ms){truncatedNote}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Search cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Search error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    private bool CanSearch() => !string.IsNullOrEmpty(SearchPattern);

    [RelayCommand]
    public void CancelSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        IsSearching = false;
    }

    [RelayCommand]
    public void ClearResults()
    {
        CancelSearch();
        Results.Clear();
        SearchPattern = "";
        ReplaceText = "";
        TotalMatchCount = 0;
        FileCount = 0;
        StatusMessage = "";
    }

    [RelayCommand]
    public void ToggleReplaceSection()
    {
        ShowReplaceSection = !ShowReplaceSection;
    }

    [RelayCommand]
    public async Task ReplaceAllAsync()
    {
        if (string.IsNullOrEmpty(SearchPattern) || string.IsNullOrEmpty(WorkingDirectory))
            return;

        if (Results.Count == 0)
            return;

        var options = new SearchOptions
        {
            CaseSensitive = CaseSensitive,
            WholeWord = WholeWord,
            UseRegex = UseRegex,
            IncludePattern = string.IsNullOrWhiteSpace(IncludePattern) ? null : IncludePattern,
            ExcludePattern = string.IsNullOrWhiteSpace(ExcludePattern) ? null : ExcludePattern,
            UseGitignore = UseGitignore
        };

        var filesToReplace = Results.Select(r => r.FullPath).ToList();

        try
        {
            var count = await _searchService.ReplaceAsync(
                SearchPattern,
                ReplaceText,
                WorkingDirectory,
                options,
                filesToReplace);

            _toastService.Show($"Replaced {count:N0} occurrences", ToastType.Success);

            // Re-search to update results
            await SearchAsync();
        }
        catch (Exception ex)
        {
            _toastService.Show($"Replace failed: {ex.Message}", ToastType.Error);
        }
    }

    [RelayCommand]
    public async Task ReplaceInFileAsync(SearchFileResultViewModel file)
    {
        if (string.IsNullOrEmpty(SearchPattern))
            return;

        var options = new SearchOptions
        {
            CaseSensitive = CaseSensitive,
            WholeWord = WholeWord,
            UseRegex = UseRegex
        };

        try
        {
            var count = await _searchService.ReplaceAsync(
                SearchPattern,
                ReplaceText,
                WorkingDirectory,
                options,
                [file.FullPath]);

            _toastService.Show($"Replaced {count:N0} occurrences in {file.RelativePath}", ToastType.Success);

            // Remove the file from results
            Results.Remove(file);
            TotalMatchCount -= file.MatchCount;
            FileCount = Results.Count;
            StatusMessage = $"{TotalMatchCount:N0} results in {FileCount:N0} files";
        }
        catch (Exception ex)
        {
            _toastService.Show($"Replace failed: {ex.Message}", ToastType.Error);
        }
    }

    [RelayCommand]
    public void ToggleFileExpanded(SearchFileResultViewModel file)
    {
        file.IsExpanded = !file.IsExpanded;
    }

    [RelayCommand]
    public void OpenFile(SearchMatchViewModel match)
    {
        OpenFileAtLineRequested?.Invoke(this, new OpenFileAtLineEventArgs(
            match.FullPath,
            match.LineNumber,
            match.Column));
    }

    [RelayCommand]
    public void OpenInExplorer(SearchFileResultViewModel file)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(file.FullPath);
            if (!string.IsNullOrEmpty(directory) && _fileSystem.DirectoryExists(directory))
            {
                _processService.Start("explorer.exe", $"/select,\"{file.FullPath}\"");
            }
        }
        catch
        {
            // Ignore
        }
    }

    [RelayCommand]
    private void HideRegexInput()
    {
        ShowRegexInput = false;
    }

    [RelayCommand(CanExecute = nameof(CanGenerateRegex))]
    private async Task GenerateRegexAsync()
    {
        var config = _configurationService.Load();
        var claudePath = Environment.ExpandEnvironmentVariables(config.Settings.CustomCommand);
        var claudeFileName = System.IO.Path.GetFileNameWithoutExtension(claudePath).ToLowerInvariant();
        if (!claudeFileName.Contains("claude") && !claudeFileName.Contains("gemini"))
        {
            _toastService.Show("AI assistant not configured — check Settings → General", ToastType.Warning);
            return;
        }

        IsGeneratingRegex = true;
        try
        {
            var prompt = $"""
                Generate a single regex pattern for the following description.
                Output ONLY the regex — no explanation, no slashes, no flags.

                Description: {RegexDescription}
                """;

            var (exitCode, output, error) = await _processService.RunAsync(
                claudePath, "-p --no-session-persistence", WorkingDirectory,
                stdin: prompt, timeout: TimeSpan.FromSeconds(30));

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var pattern = output.Trim();
                // Unwrap triple-backtick code fence (```[lang]\npattern\n```)
                if (pattern.StartsWith("```"))
                {
                    var firstNewline = pattern.IndexOf('\n');
                    if (firstNewline >= 0)
                        pattern = pattern[(firstNewline + 1)..].Trim();
                    if (pattern.EndsWith("```"))
                        pattern = pattern[..^3].Trim();
                }
                // Unwrap if the entire output is a single matching quote pair
                else if (pattern.Length >= 2 &&
                    ((pattern[0] == '`' && pattern[^1] == '`') ||
                     (pattern[0] == '"' && pattern[^1] == '"') ||
                     (pattern[0] == '\'' && pattern[^1] == '\'')))
                {
                    pattern = pattern[1..^1].Trim();
                }

                try
                {
                    _ = new System.Text.RegularExpressions.Regex(pattern);
                }
                catch (ArgumentException)
                {
                    _toastService.Show($"Generated regex is invalid — try rephrasing your description", ToastType.Error);
                    return;
                }

                SearchPattern = pattern;
                UseRegex = true;
                ShowRegexInput = false;
            }
            else if (exitCode == -1)
            {
                _toastService.Show("AI timed out — try again", ToastType.Warning);
            }
            else
            {
                var detail = !string.IsNullOrWhiteSpace(error)
                    ? error.Split('\n')[0].Trim()
                    : $"exit {exitCode}";
                _toastService.Show($"AI failed: {detail}", ToastType.Error);
            }
        }
        finally
        {
            IsGeneratingRegex = false;
        }
    }

    #endregion
}

/// <summary>
/// ViewModel wrapper for a file with search results.
/// </summary>
public partial class SearchFileResultViewModel : ObservableObject
{
    public string RelativePath { get; }
    public string FullPath { get; }
    public int MatchCount { get; }
    public ObservableCollection<SearchMatchViewModel> Matches { get; }

    [ObservableProperty]
    private bool _isExpanded = true;

    public SearchFileResultViewModel(SearchFileResult result, string workingDirectory)
    {
        RelativePath = result.RelativePath;
        FullPath = result.FullPath;
        MatchCount = result.MatchCount;
        Matches = new ObservableCollection<SearchMatchViewModel>(
            result.Matches.Select(m => new SearchMatchViewModel(m, result.FullPath)));
    }
}

/// <summary>
/// ViewModel wrapper for an individual search match.
/// </summary>
public class SearchMatchViewModel
{
    public string FullPath { get; }
    public int LineNumber { get; }
    public int Column { get; }
    public int MatchLength { get; }
    public string LineText { get; }
    public string MatchedText { get; }
    public string TextBefore { get; }
    public string TextAfter { get; }
    public List<ContextLineViewModel> ContextBefore { get; }
    public List<ContextLineViewModel> ContextAfter { get; }

    public SearchMatchViewModel(SearchMatch match, string fullPath)
    {
        FullPath = fullPath;
        LineNumber = match.LineNumber;
        Column = match.Column;
        MatchLength = match.MatchLength;
        LineText = match.LineText;
        MatchedText = match.MatchedText;
        TextBefore = match.TextBefore;
        TextAfter = match.TextAfter;
        ContextBefore = match.ContextBefore.Select(c => new ContextLineViewModel(c)).ToList();
        ContextAfter = match.ContextAfter.Select(c => new ContextLineViewModel(c)).ToList();
    }
}

/// <summary>
/// ViewModel wrapper for context lines.
/// </summary>
public class ContextLineViewModel
{
    public int LineNumber { get; }
    public string Text { get; }

    public ContextLineViewModel(ContextLine line)
    {
        LineNumber = line.LineNumber;
        Text = line.Text;
    }
}

/// <summary>
/// Event args for opening a file at a specific line.
/// </summary>
public class OpenFileAtLineEventArgs : EventArgs
{
    public string FilePath { get; }
    public int LineNumber { get; }
    public int Column { get; }

    public OpenFileAtLineEventArgs(string filePath, int lineNumber, int column = 0)
    {
        FilePath = filePath;
        LineNumber = lineNumber;
        Column = column;
    }
}
