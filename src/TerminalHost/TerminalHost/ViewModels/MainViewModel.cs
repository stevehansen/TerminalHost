using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TerminalHost.Domain;
using TerminalHost.Services;
using MessageBox = System.Windows.MessageBox;

namespace TerminalHost.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ProfileRegistry _profileRegistry;
    private readonly SessionManager _sessionManager;
    private readonly TerminalControlFactory _terminalFactory;

    [ObservableProperty]
    private ObservableCollection<TerminalPairTabViewModel> _tabs = new();

    [ObservableProperty]
    private TerminalPairTabViewModel? _selectedTab;

    public MainViewModel(ProfileRegistry profileRegistry, SessionManager sessionManager, TerminalControlFactory terminalFactory)
    {
        _profileRegistry = profileRegistry;
        _sessionManager = sessionManager;
        _terminalFactory = terminalFactory;
    }

    public void Initialize()
    {
        // Don't auto-start anything - wait for user to select a folder
    }

    [RelayCommand]
    private void OpenNewProject()
    {
        Console.WriteLine("[MainViewModel] OpenNewProject called");
        Debug.WriteLine("[MainViewModel] OpenNewProject called");

        try
        {
            // Use folder browser dialog
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Project Directory",
                ShowNewFolderButton = true,
                UseDescriptionForTitle = true
            };

            var result = dialog.ShowDialog();
            Console.WriteLine($"[MainViewModel] Dialog result: {result}");

            if (result == System.Windows.Forms.DialogResult.OK)
            {
                Console.WriteLine($"[MainViewModel] Selected path: {dialog.SelectedPath}");
                OpenProjectTab(dialog.SelectedPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainViewModel] ERROR in OpenNewProject: {ex}");
            MessageBox.Show($"Error opening project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void OpenProjectTab(string workingDirectory)
    {
        Console.WriteLine($"[MainViewModel] OpenProjectTab called with: {workingDirectory}");
        Debug.WriteLine($"[MainViewModel] OpenProjectTab called with: {workingDirectory}");

        try
        {
            // Normalize the path for comparison
            workingDirectory = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Console.WriteLine($"[MainViewModel] Normalized path: {workingDirectory}");

            if (!Directory.Exists(workingDirectory))
            {
                Console.WriteLine($"[MainViewModel] Directory not found: {workingDirectory}");
                MessageBox.Show($"Directory not found: {workingDirectory}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

        // Check if we already have a tab open for this directory
        var existingTab = Tabs.FirstOrDefault(t =>
            string.Equals(
                Path.GetFullPath(t.Pair.WorkingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                workingDirectory,
                StringComparison.OrdinalIgnoreCase));

        if (existingTab != null)
        {
            // Focus the existing tab instead of creating a new one
            SelectedTab = existingTab;
            return;
        }

        var settings = _profileRegistry.Settings;

        // Create profiles for custom command and shell
        var customProfile = new Profile
        {
            Id = "custom",
            Name = settings.CustomCommandName,
            Command = settings.CustomCommand,
            WorkingDir = workingDirectory,
            Icon = settings.CustomCommandIcon
        };

        var shellProfile = new Profile
        {
            Id = "shell",
            Name = settings.ShellCommandName,
            Command = settings.ShellCommand,
            WorkingDir = workingDirectory,
            Icon = settings.ShellCommandIcon
        };

        Debug.WriteLine($"[MainViewModel] Creating terminal pair for: {workingDirectory}");

        // Create the terminal pair
        var pair = new TerminalPair(workingDirectory, customProfile, shellProfile);

        Debug.WriteLine($"[MainViewModel] Creating custom terminal control...");
        // Create terminal controls for both
        var customControl = _terminalFactory.CreateTerminalControl(pair.CustomTerminal);
        Debug.WriteLine($"[MainViewModel] Creating shell terminal control...");
        var shellControl = _terminalFactory.CreateTerminalControl(pair.ShellTerminal);

        Debug.WriteLine($"[MainViewModel] Creating tab view model...");
        // Create view model
        var tabViewModel = new TerminalPairTabViewModel(pair, settings.CustomCommandIcon, settings.ShellCommandIcon);
        tabViewModel.SetTerminalControls(customControl, shellControl);
        tabViewModel.CloseRequested += OnTabCloseRequested;

        // Track sessions
        _sessionManager.TrackSession(pair.CustomTerminal);
        _sessionManager.TrackSession(pair.ShellTerminal);

        Debug.WriteLine($"[MainViewModel] Adding tab and selecting it...");
        Console.WriteLine($"[MainViewModel] Adding tab and selecting it...");
        Tabs.Add(tabViewModel);
        SelectedTab = tabViewModel;
        Debug.WriteLine($"[MainViewModel] Tab count: {Tabs.Count}, SelectedTab: {SelectedTab?.Title}");
        Console.WriteLine($"[MainViewModel] Tab count: {Tabs.Count}, SelectedTab: {SelectedTab?.Title}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainViewModel] ERROR in OpenProjectTab: {ex}");
            Debug.WriteLine($"[MainViewModel] ERROR in OpenProjectTab: {ex}");
            MessageBox.Show($"Error creating terminal: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CloseTab(TerminalPairTabViewModel? tab)
    {
        if (tab == null) return;

        var hasRunning = tab.Pair.CustomTerminal.IsProcessRunning() || tab.Pair.ShellTerminal.IsProcessRunning();

        if (hasRunning && _profileRegistry.Settings.ConfirmOnClose)
        {
            var result = MessageBox.Show(
                $"Terminals in '{tab.Title}' are still running. Close anyway?",
                "Confirm Close",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;
        }

        tab.CloseRequested -= OnTabCloseRequested;
        _sessionManager.CloseSession(tab.Pair.CustomTerminal);
        _sessionManager.CloseSession(tab.Pair.ShellTerminal);
        tab.Pair.Dispose();
        Tabs.Remove(tab);

        if (SelectedTab == tab && Tabs.Count > 0)
        {
            SelectedTab = Tabs[^1];
        }
    }

    [RelayCommand]
    private void SwitchActiveTerminal()
    {
        SelectedTab?.SwitchTerminalCommand.Execute(null);
    }

    [RelayCommand]
    private void ToggleSplitView()
    {
        SelectedTab?.ToggleSplitViewCommand.Execute(null);
    }

    private void OnTabCloseRequested(object? sender, EventArgs e)
    {
        if (sender is TerminalPairTabViewModel tab)
        {
            CloseTab(tab);
        }
    }

    public void Shutdown()
    {
        _sessionManager.CloseAllSessions();
        foreach (var tab in Tabs)
        {
            tab.Pair.Dispose();
        }
    }
}
