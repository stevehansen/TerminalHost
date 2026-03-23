using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TerminalHost.Views.Dialogs;

/// <summary>
/// A themed dialog window with custom button labels.
/// </summary>
public partial class CustomButtonDialog : Window
{
    /// <summary>
    /// The index of the clicked button (0-based), or -1 if dialog was closed without selection.
    /// </summary>
    public int SelectedButtonIndex { get; private set; } = -1;

    private readonly string[] _buttonLabels;

    public CustomButtonDialog(string message, string title, string[] buttons)
    {
        InitializeComponent();
        _buttonLabels = buttons;
        SetupDialog(message, title, buttons);
    }

    private void SetupDialog(string message, string title, string[] buttons)
    {
        TitleText.Text = title;
        MessageText.Text = message;

        // Generate buttons dynamically
        for (int i = 0; i < buttons.Length; i++)
        {
            var button = CreateButton(buttons[i], i);
            ButtonPanel.Children.Add(button);
        }
    }

    private Button CreateButton(string label, int index)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(16, 6, 16, 6),
            MinWidth = 80,
            Margin = new Thickness(0, 0, 8, 0),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Tag = index
        };

        // First button is primary (accent color), others are secondary
        if (index == 0)
        {
            button.Background = (Brush)FindResource("AccentPrimaryBrush");
            button.Foreground = (Brush)FindResource("TextBrightBrush");
        }
        else
        {
            button.Background = (Brush)FindResource("BackgroundLightBrush");
            button.Foreground = (Brush)FindResource("TextPrimaryBrush");
        }

        // Apply style template for hover effects
        button.Style = CreateButtonStyle(index == 0);
        button.Click += Button_Click;

        return button;
    }

    private Style CreateButtonStyle(bool isPrimary)
    {
        var style = new Style(typeof(Button));
        var template = new ControlTemplate(typeof(Button));

        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "border";
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        borderFactory.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });

        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

        borderFactory.AppendChild(contentPresenter);
        template.VisualTree = borderFactory;

        // Add triggers for hover/pressed states
        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
            isPrimary ? FindResource("AccentHoverBrush") : FindResource("BackgroundActiveBrush"),
            "border"));

        var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
            isPrimary ? FindResource("AccentDarkBrush") : FindResource("BackgroundActiveBrush"),
            "border"));

        template.Triggers.Add(hoverTrigger);
        template.Triggers.Add(pressedTrigger);

        style.Setters.Add(new Setter(System.Windows.Controls.Control.TemplateProperty, template));
        return style;
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int index)
        {
            SelectedButtonIndex = index;
            DialogResult = true;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            // Escape closes without selection (returns -1)
            SelectedButtonIndex = -1;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            // Enter selects first button
            SelectedButtonIndex = 0;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+C copies the dialog content
            CopyDialogContent();
            e.Handled = true;
        }
    }

    private void CopyDialogContent()
    {
        var buttonText = string.Join("   ", _buttonLabels);
        var separator = new string('-', 27);

        var content = $"""
            {separator}
            {TitleText.Text}
            {separator}
            {MessageText.Text}
            {separator}
            {buttonText}
            {separator}
            """;

        try
        {
            System.Windows.Clipboard.SetText(content);
        }
        catch
        {
            // Clipboard operation failed, ignore
        }
    }
}
