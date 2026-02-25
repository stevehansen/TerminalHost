using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using TerminalHost.Core.Domain;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class MergeConflictView : UserControl
{
    public MergeConflictView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnConflictFilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: GitFileStatus file } && DataContext is MergeConflictViewModel vm)
        {
            vm.SelectedConflictFile = file;
        }
    }
}
