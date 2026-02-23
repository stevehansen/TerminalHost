using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class GitTagsViewModel : ObservableObject
{
    private readonly IGitStatusService _gitStatusService;
    private readonly MainViewModel _mainViewModel;
    private readonly IDialogService _dialogService;
    private readonly IToastService _toastService;
    private readonly IProcessService _processService;
    private readonly IConfigurationService _configurationService;

    private TerminalPairTabViewModel? _currentTerminalTab;

    [ObservableProperty]
    private string _currentWorkingDirectory = string.Empty;

    [ObservableProperty]
    private ObservableCollection<GitTag> _tags = [];

    [ObservableProperty]
    private GitTag? _selectedTag;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _title = "Git Tags";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    // Create tag options
    [ObservableProperty]
    private string _newTagName = string.Empty;

    [ObservableProperty]
    private string _newTagMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SuggestVersionCommand))]
    private bool _isSuggestingVersion;

    public GitTagsViewModel(
        IGitStatusService gitStatusService,
        MainViewModel mainViewModel,
        IDialogService dialogService,
        IToastService toastService,
        IProcessService processService,
        IConfigurationService configurationService)
    {
        _gitStatusService = gitStatusService;
        _mainViewModel = mainViewModel;
        _dialogService = dialogService;
        _toastService = toastService;
        _processService = processService;
        _configurationService = configurationService;
    }

    /// <summary>
    /// Loads tag data without opening the popup.
    /// Used by the unified Git panel to load data for embedded display.
    /// </summary>
    public async Task LoadDataAsync(TerminalPairTabViewModel terminalTab)
    {
        _currentTerminalTab = terminalTab;
        CurrentWorkingDirectory = terminalTab.Pair.WorkingDirectory;
        Title = $"Git Tags - {terminalTab.Title}";
        StatusMessage = string.Empty;
        NewTagName = string.Empty;
        NewTagMessage = string.Empty;

        await RefreshTagsAsync();
    }

    [RelayCommand]
    private async Task RefreshTagsAsync()
    {
        if (string.IsNullOrEmpty(CurrentWorkingDirectory))
            return;

        IsLoading = true;
        try
        {
            var tags = await _gitStatusService.GetTagsAsync(CurrentWorkingDirectory);

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var search = SearchText.ToLower();
                tags = tags.Where(t =>
                    t.Name.ToLower().Contains(search) ||
                    (t.Message?.ToLower().Contains(search) ?? false) ||
                    (t.CommitSubject?.ToLower().Contains(search) ?? false)).ToList();
            }

            Tags = new ObservableCollection<GitTag>(tags);

            StatusMessage = Tags.Count == 0 ? "No tags found." : string.Empty;
            SelectedTag = Tags.FirstOrDefault();

            UpdateCommandsCanExecute();
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = RefreshTagsAsync();
    }

    partial void OnSelectedTagChanged(GitTag? value)
    {
        UpdateCommandsCanExecute();
    }

    private void UpdateCommandsCanExecute()
    {
        DeleteTagCommand.NotifyCanExecuteChanged();
        PushTagCommand.NotifyCanExecuteChanged();
        DeleteRemoteTagCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task CreateTagAsync()
    {
        if (string.IsNullOrEmpty(CurrentWorkingDirectory))
            return;

        if (string.IsNullOrWhiteSpace(NewTagName))
        {
            _toastService.Show("Tag name is required", ToastType.Warning);
            return;
        }

        IsLoading = true;
        try
        {
            var message = string.IsNullOrWhiteSpace(NewTagMessage) ? null : NewTagMessage.Trim();
            var result = await _gitStatusService.CreateTagAsync(CurrentWorkingDirectory, NewTagName.Trim(), message);

            if (result.Success)
            {
                _toastService.Show($"Created tag '{NewTagName.Trim()}'", ToastType.Success);
                NewTagName = string.Empty;
                NewTagMessage = string.Empty;
                await RefreshTagsAsync();
            }
            else
            {
                _dialogService.ShowWarning($"Failed to create tag:\n{result.Error}", "Git Tag");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOperateOnTag))]
    private async Task DeleteTagAsync()
    {
        if (SelectedTag == null || string.IsNullOrEmpty(CurrentWorkingDirectory))
            return;

        if (!_dialogService.ShowConfirmation(
            $"Delete local tag '{SelectedTag.Name}'?",
            "Delete Tag"))
            return;

        IsLoading = true;
        try
        {
            var result = await _gitStatusService.DeleteTagAsync(CurrentWorkingDirectory, SelectedTag.Name);

            if (result.Success)
            {
                _toastService.Show($"Deleted tag '{SelectedTag.Name}'", ToastType.Success);
                await RefreshTagsAsync();
            }
            else
            {
                _dialogService.ShowWarning($"Failed to delete tag:\n{result.Error}", "Git Tag");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOperateOnTag))]
    private async Task PushTagAsync()
    {
        if (SelectedTag == null || string.IsNullOrEmpty(CurrentWorkingDirectory))
            return;

        using var toast = _toastService.ShowProgress($"Pushing tag '{SelectedTag.Name}'...");

        try
        {
            var result = await _gitStatusService.PushTagAsync(CurrentWorkingDirectory, SelectedTag.Name);

            if (result.Success)
            {
                toast.Complete($"Pushed tag '{SelectedTag.Name}'");
            }
            else
            {
                toast.Fail($"Push failed: {result.Error}");
            }
        }
        catch
        {
            toast.Fail("Push failed");
        }
    }

    [RelayCommand]
    private async Task PushAllTagsAsync()
    {
        if (string.IsNullOrEmpty(CurrentWorkingDirectory))
            return;

        using var toast = _toastService.ShowProgress("Pushing all tags...");

        try
        {
            var result = await _gitStatusService.PushAllTagsAsync(CurrentWorkingDirectory);

            if (result.Success)
            {
                toast.Complete("All tags pushed");
            }
            else
            {
                toast.Fail($"Push failed: {result.Error}");
            }
        }
        catch
        {
            toast.Fail("Push failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanOperateOnTag))]
    private async Task DeleteRemoteTagAsync()
    {
        if (SelectedTag == null || string.IsNullOrEmpty(CurrentWorkingDirectory))
            return;

        if (!_dialogService.ShowConfirmation(
            $"Delete remote tag '{SelectedTag.Name}'?\n\nThis will remove the tag from the remote repository.",
            "Delete Remote Tag"))
            return;

        using var toast = _toastService.ShowProgress($"Deleting remote tag '{SelectedTag.Name}'...");

        try
        {
            var result = await _gitStatusService.DeleteRemoteTagAsync(CurrentWorkingDirectory, SelectedTag.Name);

            if (result.Success)
            {
                toast.Complete($"Deleted remote tag '{SelectedTag.Name}'");
            }
            else
            {
                toast.Fail($"Delete failed: {result.Error}");
            }
        }
        catch
        {
            toast.Fail("Delete failed");
        }
    }

    private bool CanOperateOnTag() => SelectedTag != null && !IsLoading;

    [RelayCommand(CanExecute = nameof(CanSuggestVersion))]
    private async Task SuggestVersionAsync()
    {
        if (string.IsNullOrEmpty(CurrentWorkingDirectory))
            return;

        var config = _configurationService.Load();
        var claudePath = Environment.ExpandEnvironmentVariables(config.Settings.CustomCommand);
        var claudeFileName = System.IO.Path.GetFileNameWithoutExtension(claudePath).ToLowerInvariant();
        if (!claudeFileName.Contains("claude") && !claudeFileName.Contains("gemini"))
        {
            _toastService.Show("AI assistant not configured — check Settings → General", ToastType.Warning);
            return;
        }

        var lastTag = Tags.FirstOrDefault();
        var lastTagName = lastTag?.Name ?? "v0.0.0";

        IsSuggestingVersion = true;
        try
        {
            var (exitCode, commitList, _) = await _processService.RunAsync(
                "git", $"log {lastTagName}..HEAD --oneline", CurrentWorkingDirectory,
                timeout: TimeSpan.FromSeconds(15));

            if (exitCode != 0 || string.IsNullOrWhiteSpace(commitList))
            {
                _toastService.Show("No new commits since last tag", ToastType.Warning);
                return;
            }

            if (commitList.Length > 6000)
                commitList = commitList[..6000] + "\n\n[... truncated ...]";

            var prompt = $"Given these commits since {lastTagName}, suggest the next semantic version number. Rules: breaking change (!) = major bump, feat = minor bump, fix/perf = patch bump. Output ONLY the version number prefixed with 'v' (e.g. v1.3.0).\n\nLast version: {lastTagName}\nCommits:\n{commitList}";

            var (aiExitCode, output, error) = await _processService.RunAsync(
                claudePath, "-p --no-session-persistence", CurrentWorkingDirectory,
                stdin: prompt, timeout: TimeSpan.FromSeconds(30));

            if (aiExitCode == -1)
            {
                _toastService.Show("AI timed out — try again", ToastType.Warning);
                return;
            }

            if (aiExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                var firstError = error.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "Unknown error";
                _toastService.Show($"AI failed: {firstError}", ToastType.Error);
                return;
            }

            var suggested = output.Trim();
            if (Regex.IsMatch(suggested, @"^v?\d+\.\d+\.\d+"))
            {
                NewTagName = suggested;
            }
            else
            {
                _toastService.Show("AI returned invalid version format", ToastType.Warning);
            }
        }
        finally
        {
            IsSuggestingVersion = false;
        }
    }

    private bool CanSuggestVersion() => !IsSuggestingVersion;

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }
}
