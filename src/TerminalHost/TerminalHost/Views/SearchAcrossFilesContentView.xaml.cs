using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class SearchAcrossFilesContentView : UserControl
{
    public SearchAcrossFilesContentView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // Focus the search box when the view is loaded
        SearchBox?.Focus();
        if (DataContext is SearchAcrossFilesViewModel vm)
            vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchAcrossFilesViewModel.ShowRegexInput) &&
            sender is SearchAcrossFilesViewModel vm && vm.ShowRegexInput)
        {
            Dispatcher.BeginInvoke(() => RegexDescriptionBox?.Focus());
        }
    }

    private void FileHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement element &&
            element.DataContext is SearchFileResultViewModel fileResult &&
            DataContext is SearchAcrossFilesViewModel vm)
        {
            vm.ToggleFileExpandedCommand.Execute(fileResult);
        }
    }

    private void MatchLine_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement element &&
            element.DataContext is SearchMatchViewModel match &&
            DataContext is SearchAcrossFilesViewModel vm)
        {
            vm.OpenFileCommand.Execute(match);
        }
    }
}
