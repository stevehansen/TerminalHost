using System.Windows;
using System.Windows.Controls;
using TerminalHost.Controls;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class MergeConflictView : UserControl
{
    private MergeConflictViewer? _conflictViewer;

    public MergeConflictView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void ConflictViewer_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is MergeConflictViewer viewer)
        {
            _conflictViewer = viewer;

            // If we already have conflict data, load it
            if (DataContext is MergeConflictViewModel vm && vm.CurrentConflict != null)
            {
                viewer.LoadConflict(vm.CurrentConflict);
            }
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MergeConflictViewModel vm)
        {
            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MergeConflictViewModel.CurrentConflict))
                {
                    _conflictViewer?.LoadConflict(vm.CurrentConflict);
                }
            };
        }
    }
}
