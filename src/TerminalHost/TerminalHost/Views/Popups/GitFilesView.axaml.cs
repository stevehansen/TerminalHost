using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using TerminalHost.Domain;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class GitFilesView : UserControl
{
    public GitFilesView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnFileItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: GitFileStatus file } && DataContext is GitFilesViewModel vm)
        {
            vm.SelectedGitFile = file;
        }
    }
}
