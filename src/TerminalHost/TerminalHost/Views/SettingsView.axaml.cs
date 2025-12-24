using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using TerminalHost.Services;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class SettingsView : UserControl
{
    private bool _isUpdatingDocument;
    private SettingsTabViewModel? _currentViewModel;
    private IDialogService? _dialogService;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += SettingsView_DataContextChanged;
        Loaded += SettingsView_Loaded;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void SettingsView_Loaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsTabViewModel viewModel)
        {
            LoadDocumentFromText(viewModel.JsonText);
        }
    }

    private void SettingsView_DataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from old view model
        if (_currentViewModel != null)
        {
            _currentViewModel.JsonTextReloaded -= OnJsonTextReloaded;
        }

        // Subscribe to new view model
        if (DataContext is SettingsTabViewModel viewModel)
        {
            _currentViewModel = viewModel;
            viewModel.JsonTextReloaded += OnJsonTextReloaded;
            LoadDocumentFromText(viewModel.JsonText);
        }
        else
        {
            _currentViewModel = null;
        }
    }

    private void OnJsonTextReloaded(object? sender, EventArgs e)
    {
        if (_currentViewModel != null)
        {
            LoadDocumentFromText(_currentViewModel.JsonText);
        }
    }

    private void LoadDocumentFromText(string jsonText)
    {
        _isUpdatingDocument = true;
        try
        {
            // For Avalonia, we'll use a TextBox instead of RichTextBox
            // JSON syntax highlighting will need to be implemented differently (e.g., AvaloniaEdit)
            var jsonEditor = this.FindControl<TextBox>("JsonEditor");
            if (jsonEditor != null)
            {
                jsonEditor.Text = jsonText;
            }
        }
        finally
        {
            _isUpdatingDocument = false;
        }
    }

    private void JsonEditor_Loaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsTabViewModel viewModel)
        {
            LoadDocumentFromText(viewModel.JsonText);
        }
    }

    private void JsonEditor_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingDocument) return;

        if (DataContext is SettingsTabViewModel viewModel)
        {
            var jsonEditor = sender as TextBox;
            if (jsonEditor != null)
            {
                viewModel.OnTextChanged(jsonEditor.Text ?? string.Empty);
            }
        }
    }

    private async void BrowseCustomCommand_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsTabViewModel viewModel)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Custom Terminal Executable",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("All files") { Patterns = new[] { "*" } }
                    }
                });

                if (files.Count > 0)
                {
                    viewModel.CustomCommand = files[0].Path.LocalPath;
                }
            }
        }
    }

    private async void BrowseCustomPath_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsTabViewModel viewModel)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Directory to Add to PATH",
                    AllowMultiple = false
                });

                if (folders.Count > 0)
                {
                    viewModel.NewCustomPath = folders[0].Path.LocalPath;
                }
            }
        }
    }

    private async void BrowseShellCommand_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsTabViewModel viewModel)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Shell Executable",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("All files") { Patterns = new[] { "*" } }
                    }
                });

                if (files.Count > 0)
                {
                    viewModel.ShellCommand = files[0].Path.LocalPath;
                }
            }
        }
    }

    private void Hyperlink_Click(object? sender, RoutedEventArgs e)
    {
        // Get URL from Tag or Content
        if (sender is Control control && control.Tag is string url)
        {
            OpenUrl(url);
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = url,
                    UseShellExecute = false
                });
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = url,
                    UseShellExecute = false
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open URL: {ex.Message}");
        }
    }

    private void GlobalCommandsFolder_Click(object? sender, RoutedEventArgs e)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var globalCommandsPath = Path.Combine(userProfile, ".claude", "commands");
        OpenOrCreateFolder(globalCommandsPath, "global commands");
    }

    private void ProjectCommandsFolder_Click(object? sender, RoutedEventArgs e)
    {
        // Get the current project directory from MainViewModel
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.DataContext is MainViewModel mainVm)
        {
            // Find the currently selected terminal tab to get its working directory
            var terminalTab = mainVm.Tabs.OfType<TerminalPairTabViewModel>().FirstOrDefault(t => t == mainVm.SelectedTab)
                           ?? mainVm.Tabs.OfType<TerminalPairTabViewModel>().FirstOrDefault();

            if (terminalTab != null)
            {
                var projectCommandsPath = Path.Combine(terminalTab.WorkingDirectory, ".claude", "commands");
                OpenOrCreateFolder(projectCommandsPath, "project commands");
            }
            else
            {
                GetDialogService()?.ShowWarning(
                    "No project is currently open.\n\nOpen a project directory first to create project-specific commands.",
                    "No Project Open");
            }
        }
    }

    private void OpenOrCreateFolder(string path, string folderDescription)
    {
        if (Directory.Exists(path))
        {
            // Open folder in file manager
            OpenFolderInFileManager(path);
        }
        else
        {
            // Ask user if they want to create it
            var dialogService = GetDialogService();
            if (dialogService != null)
            {
                var result = dialogService.ShowConfirmation(
                    $"The {folderDescription} folder does not exist:\n\n{path}\n\nWould you like to create it?",
                    "Create Folder?");

                if (result)
                {
                    try
                    {
                        Directory.CreateDirectory(path);
                        // Open the newly created folder
                        OpenFolderInFileManager(path);
                    }
                    catch (Exception ex)
                    {
                        dialogService.ShowError($"Failed to create folder:\n\n{ex.Message}", "Error");
                    }
                }
            }
        }
    }

    private void OpenFolderInFileManager(string path)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = false
                });
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = false
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open folder: {ex.Message}");
        }
    }

    private IDialogService? GetDialogService()
    {
        if (_dialogService != null) return _dialogService;

        // Try to get from SettingsTabViewModel
        if (_currentViewModel != null)
        {
            // Get via reflection since _dialogService is private
            var field = typeof(SettingsTabViewModel).GetField("_dialogService",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            _dialogService = field?.GetValue(_currentViewModel) as IDialogService;
        }

        return _dialogService;
    }
}
