using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TerminalHost.Services;

namespace TerminalHost.Views.Dialogs;

/// <summary>
/// A themed dialog window that replaces MessageBox throughout the application.
/// </summary>
public partial class NotificationDialog : Window
{
    /// <summary>
    /// The result of the dialog interaction.
    /// </summary>
    public Services.DialogResult Result { get; private set; } = Services.DialogResult.None;

    public NotificationDialog(string message, string title, DialogType type, DialogButtons buttons)
    {
        InitializeComponent();
        SetupDialog(message, title, type, buttons);
    }

    private void SetupDialog(string message, string title, DialogType type, DialogButtons buttons)
    {
        TitleText.Text = title;
        MessageText.Text = message;

        // Set icon based on type
        IconText.Text = type switch
        {
            DialogType.Error => "\u26D4",       // No entry sign
            DialogType.Warning => "\u26A0",     // Warning sign
            DialogType.Information => "\u2139", // Information
            DialogType.Question => "\u2753",    // Question mark
            _ => "\u2139"
        };

        // Set icon color based on type
        IconText.Foreground = type switch
        {
            DialogType.Error => new SolidColorBrush(Color.FromRgb(0xE7, 0x48, 0x56)),
            DialogType.Warning => new SolidColorBrush(Color.FromRgb(0xF9, 0xA8, 0x25)),
            DialogType.Information => new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)),
            DialogType.Question => new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)),
            _ => new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4))
        };

        // Configure buttons based on DialogButtons
        switch (buttons)
        {
            case DialogButtons.OK:
                OKButton.Visibility = Visibility.Visible;
                break;

            case DialogButtons.OKCancel:
                OKButton.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Visible;
                break;

            case DialogButtons.YesNo:
                YesButton.Visibility = Visibility.Visible;
                NoButton.Visibility = Visibility.Visible;
                break;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OKButton_Click(object sender, RoutedEventArgs e)
    {
        Result = Services.DialogResult.OK;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Result = Services.DialogResult.Cancel;
        Close();
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        Result = Services.DialogResult.Yes;
        Close();
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        Result = Services.DialogResult.No;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            // Escape acts like Cancel or No
            Result = CancelButton.Visibility == Visibility.Visible ? Services.DialogResult.Cancel :
                     NoButton.Visibility == Visibility.Visible ? Services.DialogResult.No :
                     Services.DialogResult.OK;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            // Enter acts like OK or Yes
            Result = OKButton.Visibility == Visibility.Visible ? Services.DialogResult.OK :
                     YesButton.Visibility == Visibility.Visible ? Services.DialogResult.Yes :
                     Services.DialogResult.None;
            Close();
            e.Handled = true;
        }
    }
}
