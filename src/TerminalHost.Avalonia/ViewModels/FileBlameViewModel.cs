using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class FileBlameViewModel : ObservableObject
{
    private readonly IGitStatusService _gitStatusService;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboardService;
    private readonly IToastService _toastService;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private string _fileName = "";

    [ObservableProperty]
    private string _workingDirectory = "";

    [ObservableProperty]
    private BlameLineViewModel? _selectedLine;

    [ObservableProperty]
    private GitCommitDetails? _selectedCommitDetails;

    [ObservableProperty]
    private bool _colorByAuthor = true;

    [ObservableProperty]
    private GitBlameResult? _blameResult;

    public ObservableCollection<BlameLineViewModel> BlameLines { get; } = [];

    public int UniqueAuthorCount => BlameResult?.UniqueAuthors.Count ?? 0;

    public FileBlameViewModel(
        IGitStatusService gitStatusService,
        IDialogService dialogService,
        IClipboardService clipboardService,
        IToastService toastService)
    {
        _gitStatusService = gitStatusService;
        _dialogService = dialogService;
        _clipboardService = clipboardService;
        _toastService = toastService;
    }

    public async Task OpenAsync(string workingDirectory, string filePath)
    {
        WorkingDirectory = workingDirectory;
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);

        BlameLines.Clear();
        SelectedLine = null;
        SelectedCommitDetails = null;

        IsOpen = true;
        await LoadBlameAsync();
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }

    [RelayCommand]
    private async Task LoadBlameAsync()
    {
        if (IsLoading || string.IsNullOrEmpty(WorkingDirectory) || string.IsNullOrEmpty(FilePath))
            return;

        IsLoading = true;

        try
        {
            BlameResult = await _gitStatusService.GetFileBlameAsync(WorkingDirectory, FilePath);

            BlameLines.Clear();

            if (BlameResult != null)
            {
                foreach (var line in BlameResult.Lines)
                {
                    // Get author color from the result
                    var authorColor = BlameResult.AuthorColors.TryGetValue(line.Author, out var color)
                        ? color
                        : "#808080";

                    BlameLines.Add(new BlameLineViewModel(line, authorColor));
                }
            }

            OnPropertyChanged(nameof(UniqueAuthorCount));
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Failed to load blame: {ex.Message}", "Error");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedLineChanged(BlameLineViewModel? value)
    {
        if (value != null)
        {
            _ = LoadCommitDetailsAsync(value.Line.CommitHash);
        }
        else
        {
            SelectedCommitDetails = null;
        }
    }

    partial void OnColorByAuthorChanged(bool value)
    {
        // Refresh colors when toggle changes
        if (BlameResult == null) return;

        foreach (var lineVm in BlameLines)
        {
            lineVm.UpdateColor(value, BlameResult.AuthorColors);
        }
    }

    private async Task LoadCommitDetailsAsync(string commitHash)
    {
        try
        {
            SelectedCommitDetails = await _gitStatusService.GetCommitDetailsAsync(WorkingDirectory, commitHash);
        }
        catch
        {
            SelectedCommitDetails = null;
        }
    }

    [RelayCommand]
    private async Task CopyHashAsync()
    {
        if (SelectedLine == null) return;

        try
        {
            await _clipboardService.SetTextAsync(SelectedLine.Line.CommitHash);
            _toastService.Show("Hash copied to clipboard", ToastType.Success);
        }
        catch
        {
            // Clipboard access can fail silently
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadBlameAsync();
    }

    [RelayCommand]
    private void ToggleColorByAuthor()
    {
        ColorByAuthor = !ColorByAuthor;
    }
}

/// <summary>
/// Wrapper ViewModel for GitBlameLine that includes author-specific coloring.
/// </summary>
public partial class BlameLineViewModel : ObservableObject
{
    public GitBlameLine Line { get; }

    [ObservableProperty]
    private string _gutterColor;

    // Expose line properties for binding
    public int LineNumber => Line.LineNumber;
    public string CommitHash => Line.CommitHash;
    public string ShortHash => Line.ShortHash;
    public string Author => Line.Author;
    public string AuthorEmail => Line.AuthorEmail;
    public DateTimeOffset CommitDate => Line.CommitDate;
    public string RelativeDate => Line.RelativeDate;
    public string Summary => Line.Summary;
    public string LineContent => Line.LineContent;
    public bool IsFirstInGroup => Line.IsFirstInGroup;
    public int GroupSize => Line.GroupSize;

    private readonly string _authorColor;

    public BlameLineViewModel(GitBlameLine line, string authorColor)
    {
        Line = line;
        _authorColor = authorColor;
        _gutterColor = authorColor; // Default to author color
    }

    /// <summary>
    /// Updates the gutter color based on color mode.
    /// </summary>
    public void UpdateColor(bool colorByAuthor, Dictionary<string, string> authorColors)
    {
        if (colorByAuthor)
        {
            GutterColor = _authorColor;
        }
        else
        {
            // Color by recency - more recent = brighter
            var age = DateTimeOffset.Now - Line.CommitDate;
            GutterColor = age.TotalDays switch
            {
                < 7 => "#4EC9B0",    // Teal (very recent)
                < 30 => "#9CDCFE",   // Light blue (recent)
                < 90 => "#DCDCAA",   // Yellow (moderate)
                < 365 => "#CE9178",  // Orange (older)
                _ => "#808080"       // Gray (very old)
            };
        }
    }
}
