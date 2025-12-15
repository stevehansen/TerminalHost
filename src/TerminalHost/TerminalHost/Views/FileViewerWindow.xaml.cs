using System.ComponentModel;
using System.Windows;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class FileViewerWindow : Window
{
    public FileViewerWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is FileViewerViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is FileViewerViewModel newVm)
        {
            newVm.IsDetached = true;
            newVm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileViewerViewModel.IsOpen))
        {
            if (DataContext is FileViewerViewModel { IsOpen: false })
            {
                Close();
            }
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is FileViewerViewModel vm)
        {
            if (vm.IsModified && vm.Mode == FileViewerMode.Edit)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Close without saving?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        base.OnClosing(e);
    }
}
