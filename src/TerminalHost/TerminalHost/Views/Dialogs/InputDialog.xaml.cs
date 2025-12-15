using System.Windows;
using System.Windows.Input;

namespace TerminalHost.Views.Dialogs;

public partial class InputDialog : Window
{
    public static readonly DependencyProperty PromptProperty =
        DependencyProperty.Register(nameof(Prompt), typeof(string), typeof(InputDialog), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty InputTextProperty =
        DependencyProperty.Register(nameof(InputText), typeof(string), typeof(InputDialog), new PropertyMetadata(string.Empty));

    public string Prompt
    {
        get => (string)GetValue(PromptProperty);
        set => SetValue(PromptProperty, value);
    }

    public string InputText
    {
        get => (string)GetValue(InputTextProperty);
        set => SetValue(InputTextProperty, value);
    }

    public bool Confirmed { get; private set; }

    public InputDialog(string prompt, string title, string defaultValue = "")
    {
        InitializeComponent();
        Prompt = prompt;
        Title = title;
        InputText = defaultValue;

        Loaded += (s, e) =>
        {
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        };
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void OKButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OKButton_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelButton_Click(sender, e);
            e.Handled = true;
        }
    }
}
