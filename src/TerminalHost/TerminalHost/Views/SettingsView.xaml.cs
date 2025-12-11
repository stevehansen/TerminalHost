using System.Windows.Controls;
using TerminalHost.Services;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    private bool _isUpdatingDocument;
    private SettingsTabViewModel? _currentViewModel;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += SettingsView_DataContextChanged;
        Loaded += SettingsView_Loaded;
    }

    private void SettingsView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsTabViewModel viewModel)
        {
            LoadDocumentFromText(viewModel.JsonText);
        }
    }

    private void SettingsView_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        // Unsubscribe from old view model
        if (_currentViewModel != null)
        {
            _currentViewModel.JsonTextReloaded -= OnJsonTextReloaded;
        }

        // Subscribe to new view model
        if (e.NewValue is SettingsTabViewModel viewModel)
        {
            _currentViewModel = viewModel;
            viewModel.JsonTextReloaded += OnJsonTextReloaded;
            LoadDocumentFromText(viewModel.JsonText);
        }
        else
        {
            _currentViewModel = null;
        }
    }

    private void OnJsonTextReloaded(object? sender, EventArgs e)
    {
        if (_currentViewModel != null)
        {
            LoadDocumentFromText(_currentViewModel.JsonText);
        }
    }

    private void LoadDocumentFromText(string jsonText)
    {
        _isUpdatingDocument = true;
        try
        {
            // Create a new FlowDocument each time (avoids the "document belongs to another RichTextBox" error)
            var document = JsonSyntaxHighlighter.CreateHighlightedDocument(jsonText);
            JsonEditor.Document = document;
        }
        finally
        {
            _isUpdatingDocument = false;
        }
    }

    private void JsonEditor_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsTabViewModel viewModel)
        {
            LoadDocumentFromText(viewModel.JsonText);
        }
    }

    private void JsonEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingDocument) return;

        if (DataContext is SettingsTabViewModel viewModel)
        {
            var currentText = JsonSyntaxHighlighter.GetPlainText(JsonEditor.Document);
            viewModel.OnTextChanged(currentText);
        }
    }
}
