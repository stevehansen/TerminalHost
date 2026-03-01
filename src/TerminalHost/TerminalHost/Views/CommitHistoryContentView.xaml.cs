using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TerminalHost.Views;

public partial class CommitHistoryContentView : UserControl
{
    public CommitHistoryContentView()
    {
        InitializeComponent();
    }

    private void OnCommitListBoxLoaded(object sender, RoutedEventArgs e)
    {
        var listScrollViewer = FindVisualChild<ScrollViewer>(CommitListBox);
        if (listScrollViewer != null)
            listScrollViewer.ScrollChanged += (_, ev) =>
                GraphScrollViewer.ScrollToVerticalOffset(ev.VerticalOffset);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found)
                return found;
            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }
}
