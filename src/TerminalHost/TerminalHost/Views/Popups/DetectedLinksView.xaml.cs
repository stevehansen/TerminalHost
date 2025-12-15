using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class DetectedLinksView : UserControl
{
    public DetectedLinksView()
    {
        InitializeComponent();
    }

    private void LinksList_PreviewKeyDown(object sender, KeyEventArgs e)
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
            if (LinksList.SelectedIndex < LinksList.Items.Count - 1)
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

    private void LinksList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is DetectedLinksViewModel viewModel && viewModel.OpenSelectedLinkCommand.CanExecute(null))
        {
            viewModel.OpenSelectedLinkCommand.Execute(null);
        }
    }
}