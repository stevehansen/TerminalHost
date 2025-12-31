using Avalonia.Controls;
using Avalonia.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class TimelineModeView : UserControl
{
    public TimelineModeView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is not TimelineTabViewModel vm)
            return;

        switch (e.Key)
        {
            case Key.Up:
                vm.NavigateIntentCommand.Execute(true);
                e.Handled = true;
                break;

            case Key.Down:
                vm.NavigateIntentCommand.Execute(false);
                e.Handled = true;
                break;

            case Key.Left:
                vm.NavigateSessionCommand.Execute(true);
                e.Handled = true;
                break;

            case Key.Right:
                vm.NavigateSessionCommand.Execute(false);
                e.Handled = true;
                break;

            case Key.Enter:
                if (vm.SelectedSession != null)
                {
                    vm.IsSessionDetailVisible = true;
                }
                e.Handled = true;
                break;

            case Key.Escape:
                if (vm.IsSessionDetailVisible)
                {
                    vm.CloseSessionDetailCommand.Execute(null);
                    e.Handled = true;
                }
                else if (vm.IsCreateIntentDialogOpen)
                {
                    vm.CloseCreateIntentDialogCommand.Execute(null);
                    e.Handled = true;
                }
                break;
        }

        // Ctrl+Alt shortcuts
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            switch (e.Key)
            {
                case Key.N:
                    vm.OpenCreateIntentDialogCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.S:
                    if (vm.SelectedIntent != null)
                    {
                        vm.SelectedIntent.StartNewSessionCommand.Execute(null);
                    }
                    e.Handled = true;
                    break;

                case Key.F:
                    if (vm.SelectedSession != null)
                    {
                        vm.SelectedSession.ForkCommand.Execute(null);
                    }
                    e.Handled = true;
                    break;
            }
        }
    }
}