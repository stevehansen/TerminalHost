using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class ScratchPadViewModel : ObservableObject
{
    private readonly IConfigurationService _configService;
    private readonly MainViewModel _mainViewModel; // Needed to get the current project
    private readonly ITimerService _timerService;
    private IPlatformTimer? _saveTimer;
    
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

        // Subscribe to MainViewModel events to handle opening requests
        _mainViewModel.ScratchPadRequested += (s, e) => Open();
        _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
    }

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
        
        // Calculate center position (approximate, since we don't have direct window access here easily)
        // Ideally the view code-behind handles centering relative to parent window
        // For now, we'll let the view handle initial placement or use default
        
        IsOpen = true;
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
        _saveTimer?.Dispose();
        _saveTimer = _timerService.CreateTimer(
            TimeSpan.FromMilliseconds(500),
            () =>
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
