using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for Scratch Pad (Ctrl+Shift+N).
/// Supports Panel, Popup, and Window display states.
/// </summary>
public partial class ScratchPadViewModel : BasePanelViewModel
{
    private readonly IConfigurationService _configService;
    private readonly MainViewModel _mainViewModel;
    private readonly ITimerService _timerService;
    private IAppTimer? _saveTimer;

    #region IPanelableViewModel Implementation

    public override string PanelId => "scratchPad";
    public override string PanelTitle => "Scratch Pad";
    public override string PanelIcon => "\uD83D\uDCDD"; // 📝
    public override PanelSizePreset SizePreset => PanelSizePreset.Medium;

    #endregion

    #region Content Properties

    [ObservableProperty]
    private string _contentText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(InfoText))]
    private bool _isGlobalScope;

    [ObservableProperty]
    private bool _isProjectScopeEnabled = true;

    public string WindowTitle => IsGlobalScope
        ? "Scratch Pad (Global)"
        : $"Scratch Pad ({(_mainViewModel.SelectedTab is TerminalPairTabViewModel t ? t.Title : "No Project")})";

    public string InfoText => IsGlobalScope
        ? "Shared across all projects"
        : (_mainViewModel.SelectedTab is TerminalPairTabViewModel t ? t.Pair.WorkingDirectory : "No active project");

    #endregion

    public ScratchPadViewModel(IConfigurationService configService, MainViewModel mainViewModel, ITimerService timerService)
    {
        _configService = configService;
        _mainViewModel = mainViewModel;
        _timerService = timerService;

        // Set defaults for scratch pad
        DisplayState = PanelDisplayState.Panel;
        Width = 600;
        Height = 450;

        _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
    }

    #region Overrides

    protected override void OnClose()
    {
        SaveContent();
        base.OnClose();
    }

    #endregion

    #region Public Methods

    public void Open()
    {
        UpdateScopeAvailability();

        if (!IsProjectScopeEnabled)
        {
            IsGlobalScope = true;
        }
        else
        {
            IsGlobalScope = false;
        }

        LoadContent();
        NotifyStateChanged();

        // Request to be shown in the appropriate mode
        RequestShow();
    }

    #endregion

    #region Commands

    [RelayCommand]
    private void Close()
    {
        OnClose();
    }

    [RelayCommand]
    private static void ToggleScope()
    {
        // Toggle logic is handled by binding to IsGlobalScope setter
    }

    #endregion

    #region Event Handlers

    private void MainViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTab))
        {
            UpdateScopeAvailability();
            if (IsOpen && !IsGlobalScope)
            {
                LoadContent();
                NotifyStateChanged();
            }
        }
    }

    partial void OnIsGlobalScopeChanged(bool value)
    {
        LoadContent();
        NotifyStateChanged();
    }

    partial void OnContentTextChanged(string value)
    {
        // Debounce save
        _saveTimer?.Stop();
        _saveTimer?.Dispose();
        _saveTimer = _timerService.CreateTimer(TimeSpan.FromMilliseconds(500), () =>
        {
            _saveTimer?.Stop();
            SaveContent();
        });
        _saveTimer.Start();
    }

    #endregion

    #region Private Methods

    private void UpdateScopeAvailability()
    {
        var hasProject = _mainViewModel.SelectedTab is TerminalPairTabViewModel;
        IsProjectScopeEnabled = hasProject;

        if (!hasProject && !IsGlobalScope)
        {
            IsGlobalScope = true;
        }
    }

    private void LoadContent()
    {
        var config = _configService.Load();

        if (IsGlobalScope)
        {
            ContentText = config.GlobalScratchPad;
        }
        else if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            var path = NormalizePath(terminalTab.Pair.WorkingDirectory);
            ContentText = config.ScratchPads.TryGetValue(path, out var c) ? c : "";
        }
        else
        {
            ContentText = "";
        }
    }

    private void SaveContent()
    {
        var config = _configService.Load();

        if (IsGlobalScope)
        {
            config.GlobalScratchPad = ContentText;
        }
        else if (_mainViewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            var path = NormalizePath(terminalTab.Pair.WorkingDirectory);
            config.ScratchPads[path] = ContentText;
        }

        _configService.Save(config);
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(InfoText));
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
    }

    #endregion
}
