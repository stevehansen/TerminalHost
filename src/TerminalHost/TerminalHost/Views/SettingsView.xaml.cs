using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using TerminalHost.Domain;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;
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

    private void SettingsView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsTabViewModel viewModel)
        {
            LoadDocumentFromText(viewModel.JsonText);
        }
    }

    private void SettingsView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Unsubscribe from old view model
        if (_currentViewModel != null)
        {
            _currentViewModel.JsonTextReloaded -= OnJsonTextReloaded;
        }

        // Subscribe to new view model
        if (e.NewValue is SettingsTabViewModel viewModel)
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
            // Create a new FlowDocument each time (avoids the "document belongs to another RichTextBox" error)
            var document = JsonSyntaxHighlighter.CreateHighlightedDocument(jsonText);
            JsonEditor.Document = document;
        }
        finally
        {
            _isUpdatingDocument = false;
        }
    }

    private void JsonEditor_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsTabViewModel viewModel)
        {
            LoadDocumentFromText(viewModel.JsonText);
        }
    }

    private void JsonEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingDocument) return;

        if (DataContext is SettingsTabViewModel viewModel)
        {
            var currentText = JsonSyntaxHighlighter.GetPlainText(JsonEditor.Document);
            viewModel.OnTextChanged(currentText);
        }
    }

    private void BrowseCustomCommand_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsTabViewModel viewModel)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Custom Terminal Executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                viewModel.CustomCommand = dialog.FileName;
            }
        }
    }

    private void BrowseShellCommand_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsTabViewModel viewModel)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Shell Executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                viewModel.ShellCommand = dialog.FileName;
            }
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        // Open URL in default browser
        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }

    private void GlobalCommandsFolder_Click(object sender, RoutedEventArgs e)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var globalCommandsPath = Path.Combine(userProfile, ".claude", "commands");
        OpenOrCreateFolder(globalCommandsPath, "global commands");
    }

    private void ProjectCommandsFolder_Click(object sender, RoutedEventArgs e)
    {
        // Get the current project directory from MainViewModel
        var mainWindow = Window.GetWindow(this);
        if (mainWindow?.DataContext is MainViewModel mainVm)
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
            // Open folder in Explorer
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
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
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{path}\"",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        dialogService.ShowError($"Failed to create folder:\n\n{ex.Message}", "Error");
                    }
                }
            }
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
