using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.Core.Domain;

namespace TerminalHost.Views;

public partial class CommandPalettePopup
{
    public event EventHandler<PaletteCommand?>? CommandSelected;
    public event EventHandler? CloseRequested;

    private List<PaletteCommand>? _allCommands;

    public CommandPalettePopup()
    {
        InitializeComponent();
    }

    public void Initialize(List<PaletteCommand> commands)
    {
        _allCommands = commands;

        // Filter commands based on CanExecute
        var availableCommands = commands
            .Where(c => c.CanExecute == null || c.CanExecute())
            .ToList();

        CommandList.ItemsSource = availableCommands;
        SearchBox.Text = "";

        if (availableCommands.Any())
        {
            CommandList.SelectedIndex = 0;
        }

        SearchBox.Focus();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_allCommands == null) return;

        var searchText = SearchBox.Text?.ToLower() ?? "";

        var filtered = _allCommands
            .Where(c => c.CanExecute == null || c.CanExecute())
            .Where(c =>
                c.Name.ToLower().Contains(searchText) ||
                (c.Description?.ToLower().Contains(searchText) ?? false) ||
                c.Category.ToLower().Contains(searchText))
            .ToList();

        CommandList.ItemsSource = filtered;

        if (filtered.Any())
        {
            CommandList.SelectedIndex = 0;
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (CommandList.SelectedIndex < CommandList.Items.Count - 1)
            {
                CommandList.SelectedIndex++;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (CommandList.SelectedIndex > 0)
            {
                CommandList.SelectedIndex--;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ExecuteSelectedCommand();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void CommandList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteSelectedCommand();
    }

    private void ExecuteSelectedCommand()
    {
        if (CommandList.SelectedItem is PaletteCommand command)
        {
            CommandSelected?.Invoke(this, command);
        }
    }
}
