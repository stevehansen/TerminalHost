using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Controls;

/// <summary>
/// A tabbed container control for hosting dockable panels.
/// Displays panels as tabs with content area for the active panel.
/// </summary>
public partial class PanelHost : UserControl
{
    public PanelHost()
    {
        InitializeComponent();
    }

    #region Dependency Properties

    /// <summary>
    /// Collection of panels to display as tabs.
    /// </summary>
    public static readonly DependencyProperty PanelsProperty =
        DependencyProperty.Register(
            nameof(Panels),
            typeof(ObservableCollection<IPanelableViewModel>),
            typeof(PanelHost),
            new PropertyMetadata(null, OnPanelsChanged));

    public ObservableCollection<IPanelableViewModel> Panels
    {
        get => (ObservableCollection<IPanelableViewModel>)GetValue(PanelsProperty);
        set => SetValue(PanelsProperty, value);
    }

    private static void OnPanelsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PanelHost host)
        {
            // If panels collection changes and no active panel, select the first one
            if (host.ActivePanel == null && host.Panels?.Count > 0)
            {
                host.ActivePanel = host.Panels[0];
            }
        }
    }

    /// <summary>
    /// The currently active/selected panel.
    /// </summary>
    public static readonly DependencyProperty ActivePanelProperty =
        DependencyProperty.Register(
            nameof(ActivePanel),
            typeof(IPanelableViewModel),
            typeof(PanelHost),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnActivePanelChanged));

    public IPanelableViewModel? ActivePanel
    {
        get => (IPanelableViewModel?)GetValue(ActivePanelProperty);
        set => SetValue(ActivePanelProperty, value);
    }

    private static void OnActivePanelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PanelHost host)
        {
            host.RaiseActivePanelChanged();
        }
    }

    /// <summary>
    /// Which side this panel host is docked on (affects some visual styling).
    /// </summary>
    public static readonly DependencyProperty SideProperty =
        DependencyProperty.Register(
            nameof(Side),
            typeof(PanelSide),
            typeof(PanelHost),
            new PropertyMetadata(PanelSide.Right));

    public PanelSide Side
    {
        get => (PanelSide)GetValue(SideProperty);
        set => SetValue(SideProperty, value);
    }

    /// <summary>
    /// Template selector for rendering panel content.
    /// </summary>
    public static readonly DependencyProperty PanelTemplateSelectorProperty =
        DependencyProperty.Register(
            nameof(PanelTemplateSelector),
            typeof(DataTemplateSelector),
            typeof(PanelHost),
            new PropertyMetadata(null));

    public DataTemplateSelector? PanelTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(PanelTemplateSelectorProperty);
        set => SetValue(PanelTemplateSelectorProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Raised when the active panel changes.
    /// </summary>
    public event EventHandler<IPanelableViewModel?>? ActivePanelChanged;

    /// <summary>
    /// Raised when the close button is clicked on a panel.
    /// The parent should handle hiding/removing the panel.
    /// </summary>
    public event EventHandler<IPanelableViewModel>? PanelCloseRequested;

    /// <summary>
    /// Raised when the undock button is clicked on a panel.
    /// The parent should handle transitioning to popup mode.
    /// </summary>
    public event EventHandler<IPanelableViewModel>? PanelUndockRequested;

    /// <summary>
    /// Raised when the pop-out button is clicked on a panel.
    /// The parent should handle transitioning to window mode.
    /// </summary>
    public event EventHandler<IPanelableViewModel>? PanelDetachRequested;

    private void RaiseActivePanelChanged()
    {
        ActivePanelChanged?.Invoke(this, ActivePanel);
    }

    #endregion

    #region Event Handlers

    private void PanelTab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is IPanelableViewModel panel)
        {
            ActivePanel = panel;
            e.Handled = true;
        }
    }

    private void UndockButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActivePanel != null)
        {
            // Raise event for parent to handle
            PanelUndockRequested?.Invoke(this, ActivePanel);
        }
    }

    private void PopOutButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActivePanel != null)
        {
            // Raise event for parent to handle
            PanelDetachRequested?.Invoke(this, ActivePanel);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActivePanel != null)
        {
            // Raise event for parent to handle
            PanelCloseRequested?.Invoke(this, ActivePanel);
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Adds a panel to this host and makes it active.
    /// </summary>
    public void AddPanel(IPanelableViewModel panel)
    {
        Panels ??= [];

        if (!Panels.Contains(panel))
        {
            Panels.Add(panel);
        }

        ActivePanel = panel;
    }

    /// <summary>
    /// Removes a panel from this host.
    /// </summary>
    public void RemovePanel(IPanelableViewModel panel)
    {
        if (Panels == null) return;

        var index = Panels.IndexOf(panel);
        if (index >= 0)
        {
            Panels.RemoveAt(index);

            // Select another panel if the removed one was active
            if (ActivePanel == panel)
            {
                if (Panels.Count > 0)
                {
                    // Select the panel at the same index, or the last one
                    ActivePanel = Panels[Math.Min(index, Panels.Count - 1)];
                }
                else
                {
                    ActivePanel = null;
                }
            }
        }
    }

    /// <summary>
    /// Activates a panel by its ID.
    /// </summary>
    public bool ActivatePanel(string panelId)
    {
        if (Panels == null) return false;

        var panel = Panels.FirstOrDefault(p => p.PanelId == panelId);
        if (panel != null)
        {
            ActivePanel = panel;
            return true;
        }

        return false;
    }

    #endregion
}
