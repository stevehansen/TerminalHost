using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.Views;

public partial class TimelineView : UserControl
{
    public TimelineView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Focus();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (DataContext is not TimelineTabViewModel vm)
            return;

        switch (e.Key)
        {
            case Key.Up:
                NavigateSession(vm, -1);
                e.Handled = true;
                break;
            case Key.Down:
                NavigateSession(vm, 1);
                e.Handled = true;
                break;
            case Key.Escape:
                if (vm.IsDetailVisible)
                {
                    vm.CloseDetailCommand.Execute(null);
                    e.Handled = true;
                }
                break;
            case Key.N when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt):
                vm.CreateNewIntentCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private static void NavigateSession(TimelineTabViewModel vm, int direction)
    {
        if (vm.Sessions.Count == 0) return;

        var currentIndex = vm.SelectedSession != null
            ? vm.Sessions.IndexOf(vm.SelectedSession)
            : -1;

        var newIndex = currentIndex + direction;
        if (newIndex < 0) newIndex = vm.Sessions.Count - 1;
        else if (newIndex >= vm.Sessions.Count) newIndex = 0;

        vm.SelectSession(vm.Sessions[newIndex]);
    }
}
