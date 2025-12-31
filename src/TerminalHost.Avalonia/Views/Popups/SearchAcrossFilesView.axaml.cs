using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class SearchAcrossFilesView : UserControl
{
    public SearchAcrossFilesView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        KeyDown += OnKeyDown;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // Focus the search box when the popup opens
        var searchBox = this.FindControl<TextBox>("SearchTextBox");
        searchBox?.Focus();
        searchBox?.SelectAll();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SearchAcrossFilesViewModel viewModel) return;

        if (e.Key == Key.Escape)
        {
            viewModel.CloseCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            viewModel.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }
}