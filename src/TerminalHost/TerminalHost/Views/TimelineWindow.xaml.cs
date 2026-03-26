using System.ComponentModel;
using System.Windows;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.Views;

public partial class TimelineWindow : Window
{
    public TimelineWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is TimelineTabViewModel oldVm)
            oldVm.CloseRequested -= OnCloseRequested;

        if (e.NewValue is TimelineTabViewModel newVm)
            newVm.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is TimelineTabViewModel vm)
        {
            vm.CloseRequested -= OnCloseRequested;
            vm.Dispose();
        }

        base.OnClosing(e);
    }
}
