using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.Views;

public partial class TimelineView : UserControl
{
    private bool _isSyncingScroll;

    public TimelineView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Set focus to enable keyboard navigation
        Focus();

        // Synchronize horizontal scroll between time ruler and swimlanes
        if (FindName("TimeRulerScroll") is ScrollViewer timeRulerScroll &&
            FindName("SwimlaneScroll") is ScrollViewer swimlaneScroll)
        {
            swimlaneScroll.ScrollChanged += (s, args) =>
            {
                if (_isSyncingScroll) return;
                _isSyncingScroll = true;
                timeRulerScroll.ScrollToHorizontalOffset(args.HorizontalOffset);
                _isSyncingScroll = false;
            };
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (DataContext is not TimelineTabViewModel viewModel)
            return;

        switch (e.Key)
        {
            case Key.Up:
                NavigateIntent(viewModel, -1);
                e.Handled = true;
                break;

            case Key.Down:
                NavigateIntent(viewModel, 1);
                e.Handled = true;
                break;

            case Key.Left:
                NavigateSession(viewModel, -1);
                e.Handled = true;
                break;

            case Key.Right:
                NavigateSession(viewModel, 1);
                e.Handled = true;
                break;

            case Key.Enter:
                // If an intent is selected but no session, select first session
                if (viewModel.SelectedIntent != null && viewModel.SelectedSession == null)
                {
                    var firstSession = viewModel.SelectedIntent.Sessions.FirstOrDefault();
                    if (firstSession != null)
                    {
                        viewModel.SelectSessionCommand.Execute(firstSession);
                    }
                }
                e.Handled = true;
                break;

            case Key.Escape:
                if (viewModel.IsSessionDetailVisible)
                {
                    viewModel.CloseSessionDetailCommand.Execute(null);
                    e.Handled = true;
                }
                break;

            // Ctrl+Alt+N: New Intent
            case Key.N when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt):
                viewModel.CreateNewIntentCommand.Execute(null);
                e.Handled = true;
                break;

            // Ctrl+Alt+S: Start session in current intent
            case Key.S when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt):
                if (viewModel.SelectedIntent != null)
                {
                    viewModel.StartSession(viewModel.SelectedIntent.Id);
                }
                e.Handled = true;
                break;

            // Ctrl+Alt+F: Fork from selected session
            case Key.F when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt):
                if (viewModel.SelectedSession != null)
                {
                    _ = viewModel.ForkSession(viewModel.SelectedSession.Id);
                }
                e.Handled = true;
                break;
        }
    }

    private static void NavigateIntent(TimelineTabViewModel viewModel, int direction)
    {
        if (viewModel.Intents.Count == 0)
            return;

        var currentIndex = viewModel.SelectedIntent != null
            ? viewModel.Intents.IndexOf(viewModel.SelectedIntent)
            : -1;

        var newIndex = currentIndex + direction;

        if (newIndex < 0)
            newIndex = viewModel.Intents.Count - 1;
        else if (newIndex >= viewModel.Intents.Count)
            newIndex = 0;

        viewModel.SelectedIntent = viewModel.Intents[newIndex];

        // Close session detail when changing intents
        if (viewModel.IsSessionDetailVisible)
        {
            viewModel.CloseSessionDetailCommand.Execute(null);
        }
    }

    private static void NavigateSession(TimelineTabViewModel viewModel, int direction)
    {
        if (viewModel.SelectedIntent == null)
            return;

        var sessions = viewModel.SelectedIntent.Sessions.ToList();
        if (sessions.Count == 0)
            return;

        var currentIndex = viewModel.SelectedSession != null
            ? sessions.FindIndex(s => s.Id == viewModel.SelectedSession.Id)
            : -1;

        var newIndex = currentIndex + direction;

        if (newIndex < 0)
            newIndex = sessions.Count - 1;
        else if (newIndex >= sessions.Count)
            newIndex = 0;

        viewModel.SelectSessionCommand.Execute(sessions[newIndex]);
    }
}
