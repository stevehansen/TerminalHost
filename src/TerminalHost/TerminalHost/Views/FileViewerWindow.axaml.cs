using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class FileViewerWindow : Window
{
    public FileViewerWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is FileViewerViewModel newVm)
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

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is FileViewerViewModel vm)
        {
            if (vm.IsModified && vm.Mode == FileViewerMode.Edit)
            {
                // Prevent immediate close
                e.Cancel = true;

                // Show confirmation dialog
                var messageBox = MessageBoxManager.GetMessageBoxStandard(
                    "Unsaved Changes",
                    "You have unsaved changes. Close without saving?",
                    ButtonEnum.YesNo);

                var result = await messageBox.ShowWindowDialogAsync(this);

                if (result == ButtonResult.Yes)
                {
                    // User confirmed - unsubscribe and close
                    vm.PropertyChanged -= OnViewModelPropertyChanged;
                    Close();
                }
                // else: User cancelled - window stays open (e.Cancel = true already set)
                return;
            }

            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        base.OnClosing(e);
    }
}
