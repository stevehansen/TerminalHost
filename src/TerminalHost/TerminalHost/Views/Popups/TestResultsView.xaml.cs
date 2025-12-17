using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

/// <summary>
/// Code-behind for TestResultsView.xaml
/// </summary>
public partial class TestResultsView : UserControl
{
    public TestResultsView()
    {
        InitializeComponent();
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
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
                if (TestResultsList.SelectedIndex < TestResultsList.Items.Count - 1)
                {
                    TestResultsList.SelectedIndex++;
                    TestResultsList.ScrollIntoView(TestResultsList.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Up:
                if (TestResultsList.SelectedIndex > 0)
                {
                    TestResultsList.SelectedIndex--;
                    TestResultsList.ScrollIntoView(TestResultsList.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.R:
                // Ctrl+R to re-run
                if (Keyboard.Modifiers == ModifierKeys.Control && !vm.IsRunning)
                {
                    _ = vm.RerunTestsAsync();
                    e.Handled = true;
                }
                break;

            case Key.F:
                // Ctrl+F to run failed only
                if (Keyboard.Modifiers == ModifierKeys.Control && !vm.IsRunning && vm.FailedCount > 0)
                {
                    _ = vm.RunFailedTestsAsync();
                    e.Handled = true;
                }
                break;
        }
    }
}
