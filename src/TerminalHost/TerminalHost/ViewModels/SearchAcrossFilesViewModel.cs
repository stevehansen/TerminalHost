using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class SearchAcrossFilesViewModel : ObservableObject
{
    private readonly ISearchService _searchService;
    private readonly IFilePreviewService _filePreviewService;
    private readonly IFileSystem _fileSystem;
    private readonly IProcessService _processService;
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
    private int _totalMatchCount;

    [ObservableProperty]
    private int _totalFileCount;

    public SearchAcrossFilesViewModel(
        ISearchService searchService,
        IFilePreviewService filePreviewService,
        IFileSystem fileSystem,
        IProcessService processService,
        IToastService toastService)
    {
        _searchService = searchService;
        _filePreviewService = filePreviewService;
        _fileSystem = fileSystem;
        _processService = processService;
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
            var results = await _searchService.SearchAsync(
                _currentWorkingDirectory,
                SearchText,
                CaseSensitive,
                WholeWord,
                UseRegex,
                string.IsNullOrWhiteSpace(IncludePattern) ? null : IncludePattern,
                string.IsNullOrWhiteSpace(ExcludePattern) ? null : ExcludePattern,
                true, // respect gitignore
                _searchCts.Token);

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

        var count = await _searchService.ReplaceAllAsync(
            _currentWorkingDirectory,
            SearchText,
            ReplaceText,
            CaseSensitive,
            WholeWord,
            UseRegex,
            string.IsNullOrWhiteSpace(IncludePattern) ? null : IncludePattern,
            string.IsNullOrWhiteSpace(ExcludePattern) ? null : ExcludePattern);

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

        var count = await _searchService.ReplaceInFileAsync(
            SelectedResult.FilePath,
            SearchText,
            ReplaceText,
            CaseSensitive,
            WholeWord,
            UseRegex);

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
            _processService.RevealInFinder(SelectedResult.FilePath);
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
