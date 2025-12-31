using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
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
    private GitBlameLine? _selectedLine;

    [ObservableProperty]
    private GitCommitDetails? _selectedCommitDetails;

    [ObservableProperty]
    private bool _colorByAuthor = true;

    public ObservableCollection<GitBlameLine> BlameLines { get; } = [];

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
            var result = await _gitStatusService.GetFileBlameAsync(WorkingDirectory, FilePath);

            BlameLines.Clear();

            if (result != null)
            {
                foreach (var line in result.Lines)
                {
                    BlameLines.Add(line);
                }
            }
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

    partial void OnSelectedLineChanged(GitBlameLine? value)
    {
        if (value != null)
        {
            _ = LoadCommitDetailsAsync(value.CommitHash);
        }
        else
        {
            SelectedCommitDetails = null;
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
            await _clipboardService.SetTextAsync(SelectedLine.CommitHash);
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
