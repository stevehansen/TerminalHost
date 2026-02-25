using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Views;

/// <summary>
/// Generic window for hosting any IPanelableViewModel content.
/// Uses DataTemplates from App.axaml to render the appropriate view.
/// </summary>
public partial class PanelWindow : Window
{
    private bool _isDocking;

    public PanelWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Event raised when the user requests to dock back to panel.
    /// </summary>
    public event EventHandler<IPanelableViewModel>? DockRequested;

    private void DockButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is IPanelableViewModel vm)
        {
            _isDocking = true;
            DockRequested?.Invoke(this, vm);
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (DataContext is IPanelableViewModel vm && !_isDocking)
        {
            vm.IsOpen = false;
        }
    }
}
