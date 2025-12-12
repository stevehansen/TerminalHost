using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.Services;
using TerminalHost.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace TerminalHost;

/// <summary>
/// Detected links popup logic.
/// </summary>
public partial class MainWindow
{
    private void DetectedLinksButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            // Refresh links before showing
            terminalTab.UpdateDetectedLinks(_viewModel.LinkDetectionService);

            // Bind to view model's detected links
            DetectedLinksList.ItemsSource = terminalTab.DetectedLinks;

            // Show/hide empty state
            DetectedLinksEmptyState.Visibility = terminalTab.DetectedLinks.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            DetectedLinksPopup.IsOpen = true;
        }
    }

    private void DetectedLinksRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            terminalTab.UpdateDetectedLinks(_viewModel.LinkDetectionService);

            // Update empty state
            DetectedLinksEmptyState.Visibility = terminalTab.DetectedLinks.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void DetectedLinksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection change - could be used for single-click open
    }

    private void DetectedLinksList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedDetectedLink();
    }

    private void DetectedLinksList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelectedDetectedLink();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DetectedLinksPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void OpenSelectedDetectedLink()
    {
        if (DetectedLinksList.SelectedItem is DetectedLink link)
        {
            _viewModel.LinkDetectionService.OpenLink(link.Url);
            DetectedLinksPopup.IsOpen = false;
        }
    }
}
