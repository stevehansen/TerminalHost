using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class DetectedLinksView : UserControl
{
    public DetectedLinksView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var viewModel = DataContext as DetectedLinksViewModel;
        if (viewModel == null) return;

        if (e.Key == Key.Enter)
        {
            viewModel.OpenSelectedLinkCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.CloseCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            if (LinksList.SelectedIndex < LinksList.ItemCount - 1)
            {
                LinksList.SelectedIndex++;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (LinksList.SelectedIndex > 0)
            {
                LinksList.SelectedIndex--;
            }
            e.Handled = true;
        }
    }

    private void LinksList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is DetectedLinksViewModel viewModel && viewModel.OpenSelectedLinkCommand.CanExecute(null))
        {
            viewModel.OpenSelectedLinkCommand.Execute(null);
        }
    }
}