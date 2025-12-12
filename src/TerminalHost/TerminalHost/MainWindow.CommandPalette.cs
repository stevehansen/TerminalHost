using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.Domain;
using TerminalHost.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

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
                Execute = ShowTabSwitcher
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
                Execute = OpenFilePreviewDialog
            },
            new PaletteCommand
            {
                Id = "file-edit",
                Name = "Edit File",
                Description = "Open file in editor",
                Shortcut = "Ctrl+Shift+E",
                Icon = "✏️",
                Category = "File",
                Execute = OpenFileEditDialog
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
                Execute = ShowScratchPad
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
                Execute = ShowGitFiles,
                CanExecute = () => _viewModel.SelectedTab is TerminalPairTabViewModel
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
