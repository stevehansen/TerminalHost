using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.Domain;
using TerminalHost.ViewModels;

namespace TerminalHost;

/// <summary>
/// Command palette popup logic.
/// </summary>
public partial class MainWindow
{
    private List<PaletteCommand> _paletteCommands = new();

    private void InitializeCommandPalette()
    {
        _paletteCommands = new List<PaletteCommand>
        {
            // Tab/Project commands
            new PaletteCommand
            {
                Id = "new-project",
                Name = "New Project",
                Description = "Open folder as new project",
                Shortcut = "Ctrl+N",
                Icon = "📁",
                Category = "Project",
                Execute = () => _viewModel.OpenNewProjectCommand.Execute(null)
            },
            new PaletteCommand
            {
                Id = "close-tab",
                Name = "Close Tab",
                Description = "Close current tab",
                Shortcut = "Ctrl+W",
                Icon = "✕",
                Category = "Tab",
                Execute = () => { if (_viewModel.SelectedTab != null) _viewModel.CloseTabCommand.Execute(_viewModel.SelectedTab); }
            },
            new PaletteCommand
            {
                Id = "tab-switcher",
                Name = "Switch Tab",
                Description = "Search and switch tabs",
                Shortcut = "Ctrl+Shift+T",
                Icon = "🔍",
                Category = "Tab",
                Execute = () => { _viewModel.IsTabSwitcherOpen = true; _viewModel.SwitcherSearchText = ""; }
            },

            // File commands
            new PaletteCommand
            {
                Id = "file-preview",
                Name = "Preview File",
                Description = "Open file preview",
                Shortcut = "Ctrl+O",
                Icon = "👁",
                Category = "File",
                Execute = () =>
                {
                    CenterFilePreviewPopup();
                    var initialDir = _viewModel.SelectedTab is TerminalPairTabViewModel terminalTab
                        ? terminalTab.Pair.WorkingDirectory
                        : string.Empty;
                    _filePreviewViewModel.OpenDialogCommand.Execute(initialDir);
                }
            },
            new PaletteCommand
            {
                Id = "file-edit",
                Name = "Edit File",
                Description = "Open file in editor",
                Shortcut = "Ctrl+Shift+E",
                Icon = "✏️",
                Category = "File",
                Execute = () =>
                {
                    CenterFileEditPopup();
                    var initialDir = _viewModel.SelectedTab is TerminalPairTabViewModel terminalTab
                        ? terminalTab.Pair.WorkingDirectory
                        : string.Empty;
                    _fileEditViewModel.OpenDialogCommand.Execute(initialDir);
                }
            },
            new PaletteCommand
            {
                Id = "open-explorer",
                Name = "Open in Explorer",
                Description = "Open folder in file explorer",
                Shortcut = "Ctrl+E",
                Icon = "📂",
                Category = "File",
                Execute = () => _viewModel.OpenInExplorerCommand.Execute(null),
                CanExecute = () => _viewModel.SelectedTab is TerminalPairTabViewModel
            },

            // Terminal commands
            new PaletteCommand
            {
                Id = "switch-terminal",
                Name = "Switch Terminal",
                Description = "Toggle between custom and shell",
                Shortcut = "Ctrl+`",
                Icon = "⇄",
                Category = "Terminal",
                Execute = () => _viewModel.SwitchActiveTerminalCommand.Execute(null),
                CanExecute = () => _viewModel.SelectedTab is TerminalPairTabViewModel
            },

            // Settings
            new PaletteCommand
            {
                Id = "settings",
                Name = "Settings",
                Description = "Open settings editor",
                Shortcut = "Ctrl+,",
                Icon = "⚙️",
                Category = "Settings",
                Execute = () => _viewModel.OpenSettingsCommand.Execute(null)
            },
            new PaletteCommand
            {
                Id = "profiles",
                Name = "Profiles",
                Description = "Manage terminal profiles",
                Shortcut = "Ctrl+P",
                Icon = "👤",
                Category = "Settings",
                Execute = () => _viewModel.OpenProfilesCommand.Execute(null)
            },

            // Help
            new PaletteCommand
            {
                Id = "help",
                Name = "Help",
                Description = "Show keyboard shortcuts",
                Shortcut = "F1",
                Icon = "❓",
                Category = "Help",
                Execute = () => HelpPopup.IsOpen = true
            },

            // Scratch Pad
            new PaletteCommand
            {
                Id = "scratch-pad",
                Name = "Scratch Pad",
                Description = "Open notes panel",
                Shortcut = "Ctrl+Shift+N",
                Icon = "📝",
                Category = "Tools",
                Execute = () => _viewModel.OpenScratchPadCommand.Execute(null)
            },

            // Statistics
            new PaletteCommand
            {
                Id = "statistics",
                Name = "Statistics",
                Description = "View usage statistics",
                Icon = "📊",
                Category = "Tools",
                Execute = () => _viewModel.OpenStatisticsCommand.Execute(null)
            },

            // Git
            new PaletteCommand
            {
                Id = "git-changes",
                Name = "Git Changes",
                Description = "View modified files and diffs",
                Shortcut = "Ctrl+G",
                Icon = "📋",
                Category = "Git",
                Execute = () => _viewModel.OpenGitChangesCommand.Execute(null),
                CanExecute = () => _viewModel.SelectedTab is TerminalPairTabViewModel
            },
            new PaletteCommand
            {
                Id = "git-branches",
                Name = "Git Branches",
                Description = "Switch, create, or delete branches",
                Shortcut = "Ctrl+B",
                Icon = "🌿",
                Category = "Git",
                Execute = async () => await _gitBranchViewModel.OpenAsync(),
                CanExecute = () => _viewModel.SelectedTab is TerminalPairTabViewModel
            },

            // Run commands
            new PaletteCommand
            {
                Id = "run-start",
                Name = "Run: Start",
                Description = "Start the project",
                Shortcut = "F5",
                Icon = "▶",
                Category = "Run",
                Execute = () => { if (_viewModel.SelectedTab is TerminalPairTabViewModel tab && tab.CanRun) tab.StartRunCommand.Execute(null); },
                CanExecute = () => _viewModel.SelectedTab is TerminalPairTabViewModel { CanRun: true }
            },
            new PaletteCommand
            {
                Id = "run-stop",
                Name = "Run: Stop",
                Description = "Stop the running project",
                Shortcut = "Shift+F5",
                Icon = "⏹",
                Category = "Run",
                Execute = () => { if (_viewModel.SelectedTab is TerminalPairTabViewModel tab && tab.CanStop) tab.StopRunCommand.Execute(null); },
                CanExecute = () => _viewModel.SelectedTab is TerminalPairTabViewModel { CanStop: true }
            },
            new PaletteCommand
            {
                Id = "run-restart",
                Name = "Run: Restart",
                Description = "Restart the running project",
                Icon = "🔄",
                Category = "Run",
                Execute = () => { if (_viewModel.SelectedTab is TerminalPairTabViewModel tab) tab.RestartRunCommand.Execute(null); },
                CanExecute = () => _viewModel.SelectedTab is TerminalPairTabViewModel { RunState: RunState.Running }
            },
            new PaletteCommand
            {
                Id = "run-toggle-terminal",
                Name = "Run: Toggle Terminal",
                Description = "Show/hide run terminal panel",
                Icon = "📺",
                Category = "Run",
                Execute = () => { if (_viewModel.SelectedTab is TerminalPairTabViewModel tab) tab.ToggleRunTerminalCommand.Execute(null); },
                CanExecute = () => _viewModel.SelectedTab is TerminalPairTabViewModel
            },
            new PaletteCommand
            {
                Id = "run-open-url",
                Name = "Run: Open URL",
                Description = "Open detected localhost URL in browser",
                Icon = "🌐",
                Category = "Run",
                Execute = () => { if (_viewModel.SelectedTab is TerminalPairTabViewModel tab && !string.IsNullOrEmpty(tab.DetectedRunUrl)) _viewModel.RunUrlDetectionService.OpenInBrowser(tab.DetectedRunUrl); },
                CanExecute = () => _viewModel.SelectedTab is TerminalPairTabViewModel { HasDetectedRunUrl: true }
            }
        };
    }

    private void ShowCommandPalette()
    {
        if (_paletteCommands.Count == 0)
        {
            InitializeCommandPalette();
        }

        // Filter commands based on CanExecute
        var availableCommands = _paletteCommands
            .Where(c => c.CanExecute == null || c.CanExecute())
            .ToList();

        PaletteCommandList.ItemsSource = availableCommands;
        PaletteSearchBox.Text = "";

        if (availableCommands.Any())
        {
            PaletteCommandList.SelectedIndex = 0;
        }

        CommandPalettePopup.IsOpen = true;
        PaletteSearchBox.Focus();
    }

    private void PaletteSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = PaletteSearchBox.Text?.ToLower() ?? "";

        var filtered = _paletteCommands
            .Where(c => c.CanExecute == null || c.CanExecute())
            .Where(c =>
                c.Name.ToLower().Contains(searchText) ||
                (c.Description?.ToLower().Contains(searchText) ?? false) ||
                c.Category.ToLower().Contains(searchText))
            .ToList();

        PaletteCommandList.ItemsSource = filtered;

        if (filtered.Any())
        {
            PaletteCommandList.SelectedIndex = 0;
        }
    }

    private void PaletteSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (PaletteCommandList.SelectedIndex < PaletteCommandList.Items.Count - 1)
            {
                PaletteCommandList.SelectedIndex++;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (PaletteCommandList.SelectedIndex > 0)
            {
                PaletteCommandList.SelectedIndex--;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ExecuteSelectedPaletteCommand();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CommandPalettePopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void PaletteCommandList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteSelectedPaletteCommand();
    }

    private void ExecuteSelectedPaletteCommand()
    {
        if (PaletteCommandList.SelectedItem is PaletteCommand command)
        {
            CommandPalettePopup.IsOpen = false;
            command.Execute();
        }
    }
}
