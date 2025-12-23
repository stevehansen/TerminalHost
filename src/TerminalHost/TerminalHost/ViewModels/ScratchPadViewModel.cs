using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Domain;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for Scratch Pad (Ctrl+Shift+N).
/// Supports Panel, Popup, and Window display states.
/// </summary>
public partial class ScratchPadViewModel : ObservableObject, IPanelableViewModel
{
    private readonly IConfigurationService _configService;
    private readonly MainViewModel _mainViewModel; // Needed to get the current project
    private readonly ITimerService _timerService;
    private IAppTimer? _saveTimer;

    #region IPanelableViewModel Implementation

    public string PanelId => "scratchPad";
    public string PanelTitle => "Scratch Pad";
    public string PanelIcon => "\uD83D\uDCDD"; // 📝

    public IEnumerable<PanelHeaderCommand>? HeaderCommands => null;
    public string? StatusText => null;

    [ObservableProperty]
    private PanelDisplayState _displayState = PanelDisplayState.Panel;

    [ObservableProperty]
    private PanelSide _preferredSide = PanelSide.Right;

    public ICommand DockCommand { get; private set; } = null!;
    public ICommand UndockCommand { get; private set; } = null!;
    public ICommand DetachCommand { get; private set; } = null!;
    ICommand IPanelableViewModel.CloseCommand => CloseCommand;

    public event EventHandler<PanelStateChangeRequestedEventArgs>? StateChangeRequested;

    /// <summary>
    /// Event raised when the panel needs to be shown.
    /// </summary>
    public event EventHandler? ShowRequested;

    #endregion

    [ObservableProperty]
    private string _contentText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(InfoText))]
    private bool _isGlobalScope;

    [ObservableProperty]
    private bool _isProjectScopeEnabled = true;

    [ObservableProperty]
    private bool _isOpen;

    // View properties needed for bindings
    [ObservableProperty]
    private double _width = 600;

    [ObservableProperty]
    private double _height = 450;

    public PanelSizePreset SizePreset => PanelSizePreset.Medium;

    // Position properties for manual popup placement
    [ObservableProperty]
    private double _horizontalOffset;
    
    [ObservableProperty]
    private double _verticalOffset;

    public string WindowTitle => IsGlobalScope 
        ? "Scratch Pad (Global)" 
        : $"Scratch Pad ({(_mainViewModel.SelectedTab is TerminalPairTabViewModel t ? t.Title : "No Project")})";

    public string InfoText => IsGlobalScope 
        ? "Shared across all projects" 
        : (_mainViewModel.SelectedTab is TerminalPairTabViewModel t ? t.Pair.WorkingDirectory : "No active project");

    public ScratchPadViewModel(IConfigurationService configService, MainViewModel mainViewModel, ITimerService timerService)
    {
        _configService = configService;
        _mainViewModel = mainViewModel;
        _timerService = timerService;

        // Initialize panel commands
        DockCommand = new RelayCommand<PanelSide?>(OnDock);
        UndockCommand = new RelayCommand(OnUndock);
        DetachCommand = new RelayCommand(OnDetach);

        // Note: MainWindow handles ScratchPadRequested to control DisplayState
        // We only subscribe to PropertyChanged for scope updates
        _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
    }

    #region Panel Command Handlers

    private void OnDock(PanelSide? side)
    {
        var dockSide = side ?? PreferredSide;
        StateChangeRequested?.Invoke(this, new PanelStateChangeRequestedEventArgs(PanelDisplayState.Panel, dockSide));
    }

    private void OnUndock()
    {
        StateChangeRequested?.Invoke(this, new PanelStateChangeRequestedEventArgs(PanelDisplayState.Popup));
        // After state change removes from docked panels, request to show as popup
        ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnDetach()
    {
        StateChangeRequested?.Invoke(this, new PanelStateChangeRequestedEventArgs(PanelDisplayState.Window));
        // After state change removes from docked panels, request to show as window
        ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sets the display state directly (called by panel host when state changes are applied).
    /// </summary>
    public void SetDisplayState(PanelDisplayState state, PanelSide? side = null)
    {
        DisplayState = state;
        if (side.HasValue)
        {
            PreferredSide = side.Value;
        }
    }

    #endregion

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

    public void Open()
    {
        UpdateScopeAvailability();
        
        // Default to project scope if available and previously selected or default
        // But logic in original code: if no project, force global. If project, force project.
        if (!IsProjectScopeEnabled)
        {
            IsGlobalScope = true;
        }
        else
        {
            // If we have a project, default to project scope when opening
            IsGlobalScope = false;
        }

        LoadContent();
        NotifyStateChanged();

        // Request to be shown in the appropriate mode
        // NOTE: Don't set IsOpen here - let the ShowRequested handler set it based on DisplayState
        // This prevents the popup from showing when we want Panel or Window mode
        ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Close()
    {
        SaveContent();
        IsOpen = false;
    }

    [RelayCommand]
    private static void ToggleScope()
    {
        // Toggle logic is handled by binding to IsGlobalScope setter
        // When IsGlobalScope changes, LoadContent is called
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
}
