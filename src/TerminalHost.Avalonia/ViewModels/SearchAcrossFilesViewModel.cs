using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
using TerminalHost.Services;
using SearchMatch = TerminalHost.Domain.SearchMatch;

namespace TerminalHost.ViewModels;

public partial class SearchAcrossFilesViewModel : ObservableObject
{
    private readonly ISearchService _searchService;
    private readonly IFilePreviewService _filePreviewService;
    private readonly IFileSystem _fileSystem;
    private readonly IProcessService _processService;
    private readonly IConfigurationService _configurationService;
    private readonly IToastService _toastService;

    private CancellationTokenSource? _searchCts;
    private string _currentWorkingDirectory = string.Empty;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isDragging;

    [ObservableProperty]
    private double _width = 900;

    [ObservableProperty]
    private double _height = 600;

    [ObservableProperty]
    private double _horizontalOffset;

    [ObservableProperty]
    private double _verticalOffset;

    [ObservableProperty]
    private string _title = "Search Across Files";

    [ObservableProperty]
    private string _info = "";

    [ObservableProperty]
    private string _searchText = "";

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
    private string _excludePattern = "";

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _isReplaceMode;

    [ObservableProperty]
    private ObservableCollection<SearchResult> _searchResults = [];

    [ObservableProperty]
    private SearchResult? _selectedResult;

    [ObservableProperty]
    private SearchMatch? _selectedMatch;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private bool _showRegexInput;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateRegexCommand))]
    private string _regexDescription = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateRegexCommand))]
    private bool _isGeneratingRegex;

    [ObservableProperty]
    private int _totalMatchCount;

    [ObservableProperty]
    private int _totalFileCount;

    public SearchAcrossFilesViewModel(
        ISearchService searchService,
        IFilePreviewService filePreviewService,
        IFileSystem fileSystem,
        IProcessService processService,
        IConfigurationService configurationService,
        IToastService toastService)
    {
        _searchService = searchService;
        _filePreviewService = filePreviewService;
        _fileSystem = fileSystem;
        _processService = processService;
        _configurationService = configurationService;
        _toastService = toastService;
    }

    [RelayCommand]
    public void Open(TerminalPairTabViewModel terminalTab)
    {
        _currentWorkingDirectory = terminalTab.Pair.WorkingDirectory;
        Title = $"Search - {terminalTab.Title}";
        Info = _currentWorkingDirectory;

        // Clear previous results but keep search options
        SearchResults.Clear();
        SelectedResult = null;
        SelectedMatch = null;
        StatusText = "";
        HasResults = false;
        TotalMatchCount = 0;
        TotalFileCount = 0;

        IsOpen = true;
    }

    [RelayCommand]
    private void Close()
    {
        CancelSearch();
        IsOpen = false;
        SelectedResult = null;
        SelectedMatch = null;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText) || string.IsNullOrEmpty(_currentWorkingDirectory))
            return;

        CancelSearch();
        _searchCts = new CancellationTokenSource();

        IsSearching = true;
        StatusText = "Searching...";
        SearchResults.Clear();
        HasResults = false;
        TotalMatchCount = 0;
        TotalFileCount = 0;

        try
        {
            var options = new SearchOptions
            {
                CaseSensitive = CaseSensitive,
                WholeWord = WholeWord,
                UseRegex = UseRegex,
                IncludePattern = string.IsNullOrWhiteSpace(IncludePattern) ? null : IncludePattern,
                ExcludePattern = string.IsNullOrWhiteSpace(ExcludePattern) ? null : ExcludePattern,
                UseGitignore = true
            };
            var searchResults = await _searchService.SearchAsync(
                SearchText,
                _currentWorkingDirectory,
                options,
                null,
                _searchCts.Token);
            var results = searchResults.Files.Select(f => new SearchResult
            {
                FilePath = f.FullPath,
                RelativePath = f.RelativePath,
                Matches = f.Matches.Select(m => new Domain.SearchMatch
                {
                    LineNumber = m.LineNumber,
                    LineContent = m.LineContent,
                    MatchStart = m.MatchStart,
                    MatchLength = m.MatchLength
                }).ToList()
            }).ToList();

            SearchResults = new ObservableCollection<SearchResult>(results);
            HasResults = results.Count > 0;
            TotalFileCount = results.Count;
            TotalMatchCount = results.Sum(r => r.MatchCount);

            StatusText = HasResults
                ? $"{TotalMatchCount} matches in {TotalFileCount} files"
                : "No matches found";

            if (SearchResults.Count > 0)
            {
                SelectedResult = SearchResults[0];
                if (SelectedResult.Matches.Count > 0)
                {
                    SelectedMatch = SelectedResult.Matches[0];
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Search cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void CancelSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
    }

    [RelayCommand]
    private void ToggleReplaceMode()
    {
        IsReplaceMode = !IsReplaceMode;
    }

    [RelayCommand(CanExecute = nameof(CanReplaceAll))]
    private async Task ReplaceAllAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText) || string.IsNullOrEmpty(_currentWorkingDirectory))
            return;

        if (!HasResults)
        {
            await SearchAsync();
            if (!HasResults) return;
        }

        var options = new SearchOptions
        {
            CaseSensitive = CaseSensitive,
            WholeWord = WholeWord,
            UseRegex = UseRegex,
            IncludePattern = string.IsNullOrWhiteSpace(IncludePattern) ? null : IncludePattern,
            ExcludePattern = string.IsNullOrWhiteSpace(ExcludePattern) ? null : ExcludePattern
        };

        var count = await _searchService.ReplaceAsync(
            SearchText,
            ReplaceText,
            _currentWorkingDirectory,
            options);

        _toastService.Show($"Replaced {count} occurrences", ToastType.Success);

        // Refresh search results
        await SearchAsync();
    }

    public bool CanReplaceAll => HasResults && !string.IsNullOrEmpty(SearchText);

    [RelayCommand(CanExecute = nameof(CanReplaceInFile))]
    private async Task ReplaceInFileAsync()
    {
        if (SelectedResult == null || string.IsNullOrWhiteSpace(SearchText))
            return;

        var options = new SearchOptions
        {
            CaseSensitive = CaseSensitive,
            WholeWord = WholeWord,
            UseRegex = UseRegex
        };

        var count = await _searchService.ReplaceAsync(
            SearchText,
            ReplaceText,
            _currentWorkingDirectory,
            options,
            [SelectedResult.FilePath]);

        _toastService.Show($"Replaced {count} occurrences in {SelectedResult.FileName}", ToastType.Success);

        // Refresh search results
        await SearchAsync();
    }

    public bool CanReplaceInFile => SelectedResult != null && !string.IsNullOrEmpty(SearchText);

    [RelayCommand(CanExecute = nameof(CanOpenFile))]
    private void OpenFile()
    {
        if (SelectedResult == null) return;

        FilePreviewRequested?.Invoke(this, new FilePreviewRequestedEventArgs
        {
            FilePath = SelectedResult.FilePath,
            Line = SelectedMatch?.LineNumber
        });
    }

    [RelayCommand(CanExecute = nameof(CanOpenFile))]
    private void EditFile()
    {
        if (SelectedResult == null) return;

        FileEditRequested?.Invoke(this, new FileEditRequestedEventArgs
        {
            FilePath = SelectedResult.FilePath,
            LineNumber = SelectedMatch?.LineNumber
        });
    }

    public bool CanOpenFile => SelectedResult != null;

    [RelayCommand(CanExecute = nameof(CanOpenFile))]
    private void RevealInExplorer()
    {
        if (SelectedResult == null) return;

        if (_fileSystem.FileExists(SelectedResult.FilePath))
        {
            _processService.RevealInFolder(SelectedResult.FilePath);
        }
    }

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var result in SearchResults)
        {
            result.IsExpanded = true;
        }
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var result in SearchResults)
        {
            result.IsExpanded = false;
        }
    }

    [RelayCommand]
    private void ToggleRegexInput()
    {
        ShowRegexInput = !ShowRegexInput;
        if (ShowRegexInput)
            RegexDescription = "";
    }

    [RelayCommand]
    private void HideRegexInput()
    {
        ShowRegexInput = false;
        RegexDescription = "";
    }

    [RelayCommand(CanExecute = nameof(CanGenerateRegex))]
    private async Task GenerateRegexAsync()
    {
        var config = _configurationService.Load();
        var claudePath = Environment.ExpandEnvironmentVariables(config.Settings.CustomCommand);
        var claudeFileName = Path.GetFileNameWithoutExtension(claudePath).ToLowerInvariant();
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
                claudePath, "-p --no-session-persistence", _currentWorkingDirectory,
                stdin: prompt, timeout: TimeSpan.FromSeconds(30));

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var pattern = UnwrapCodeFences(output);
                pattern = UnwrapMatchingQuotes(pattern);

                try
                {
                    _ = new Regex(pattern);
                }
                catch (ArgumentException)
                {
                    _toastService.Show("Generated regex is invalid — try rephrasing your description", ToastType.Error);
                    return;
                }

                SearchText = pattern;
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

    public bool CanGenerateRegex => !IsGeneratingRegex && !string.IsNullOrWhiteSpace(RegexDescription);

    private static string UnwrapCodeFences(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                var inner = trimmed[(firstNewline + 1)..];
                if (inner.TrimEnd().EndsWith("```"))
                {
                    var lastFence = inner.LastIndexOf("```");
                    if (lastFence >= 0)
                        return inner[..lastFence].Trim();
                }
            }
        }
        return trimmed;
    }

    private static string UnwrapMatchingQuotes(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length >= 2)
        {
            var first = trimmed[0];
            var last = trimmed[^1];
            if ((first == '`' && last == '`') ||
                (first == '"' && last == '"') ||
                (first == '\'' && last == '\''))
            {
                return trimmed[1..^1];
            }
        }
        return trimmed;
    }

    partial void OnSearchTextChanged(string value)
    {
        // Auto-search after a short delay when typing
        // For now, require explicit search action
        ReplaceAllCommand.NotifyCanExecuteChanged();
        ReplaceInFileCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedResultChanged(SearchResult? value)
    {
        OpenFileCommand.NotifyCanExecuteChanged();
        EditFileCommand.NotifyCanExecuteChanged();
        RevealInExplorerCommand.NotifyCanExecuteChanged();
        ReplaceInFileCommand.NotifyCanExecuteChanged();

        // Select first match when file is selected
        if (value?.Matches.Count > 0)
        {
            SelectedMatch = value.Matches[0];
        }
    }

    partial void OnHasResultsChanged(bool value)
    {
        ReplaceAllCommand.NotifyCanExecuteChanged();
    }

    public event EventHandler<FilePreviewRequestedEventArgs>? FilePreviewRequested;
    public event EventHandler<FileEditRequestedEventArgs>? FileEditRequested;
}
