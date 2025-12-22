using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TerminalHost.Views.Dialogs;

public partial class InputDialog : Window
{
    public static readonly StyledProperty<string> PromptProperty =
        AvaloniaProperty.Register<InputDialog, string>(nameof(Prompt), string.Empty);

    public static readonly StyledProperty<string> InputTextProperty =
        AvaloniaProperty.Register<InputDialog, string>(nameof(InputText), string.Empty);

    public string Prompt
    {
        get => GetValue(PromptProperty);
        set => SetValue(PromptProperty, value);
    }

    public string InputText
    {
        get => GetValue(InputTextProperty);
        set => SetValue(InputTextProperty, value);
    }

    public bool Confirmed { get; private set; }

    public InputDialog()
    {
        InitializeComponent();
    }

    public InputDialog(string prompt, string title, string defaultValue = "")
    {
        InitializeComponent();
        Prompt = prompt;
        Title = title;
        InputText = defaultValue;

        Opened += (s, e) =>
        {
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        };
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OKButton_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }

    private void InputTextBox_KeyDown(object? sender, KeyEventArgs e)
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
