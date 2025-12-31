using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

/// <summary>
/// Code-behind for TestResultsView.axaml
/// </summary>
public partial class TestResultsView : UserControl
{
    public TestResultsView()
    {
        InitializeComponent();
        KeyDown += UserControl_KeyDown;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void UserControl_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not TestResultsViewModel vm)
            return;

        switch (e.Key)
        {
            case Key.Escape:
                vm.CloseCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F6:
                // Re-run tests
                if (!vm.IsRunning)
                {
                    _ = vm.RerunTestsAsync();
                    e.Handled = true;
                }
                break;

            case Key.Down:
                var listBox = this.FindControl<ListBox>("TestResultsList");
                if (listBox != null && listBox.SelectedIndex < listBox.ItemCount - 1)
                {
                    listBox.SelectedIndex++;
                    listBox.ScrollIntoView(listBox.SelectedIndex);
                }
                e.Handled = true;
                break;

            case Key.Up:
                var listBoxUp = this.FindControl<ListBox>("TestResultsList");
                if (listBoxUp != null && listBoxUp.SelectedIndex > 0)
                {
                    listBoxUp.SelectedIndex--;
                    listBoxUp.ScrollIntoView(listBoxUp.SelectedIndex);
                }
                e.Handled = true;
                break;

            case Key.R:
                // Ctrl+R to re-run
                if (e.KeyModifiers == KeyModifiers.Control && !vm.IsRunning)
                {
                    _ = vm.RerunTestsAsync();
                    e.Handled = true;
                }
                break;

            case Key.F:
                // Ctrl+F to run failed only
                if (e.KeyModifiers == KeyModifiers.Control && !vm.IsRunning && vm.FailedCount > 0)
                {
                    _ = vm.RunFailedTestsAsync();
                    e.Handled = true;
                }
                break;
        }
    }
}