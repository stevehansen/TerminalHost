using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.Services;

namespace TerminalHost.Views;

public partial class DetectedLinksPopup : UserControl
{
    public event EventHandler<DetectedLink?>? LinkSelected;
    public event EventHandler? RefreshRequested;
    public event EventHandler? CloseRequested;

    public DetectedLinksPopup()
    {
        InitializeComponent();
    }

    public void Initialize(ObservableCollection<DetectedLink> links)
    {
        LinksList.ItemsSource = links;
        UpdateEmptyState(links.Count == 0);
    }

    public void UpdateEmptyState(bool isEmpty)
    {
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void LinksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection change - could be used for single-click open
    }

    private void LinksList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedLink();
    }

    private void LinksList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelectedLink();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void OpenSelectedLink()
    {
        if (LinksList.SelectedItem is DetectedLink link)
        {
            LinkSelected?.Invoke(this, link);
        }
    }
}
