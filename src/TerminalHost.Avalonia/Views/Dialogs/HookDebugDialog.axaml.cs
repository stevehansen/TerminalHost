using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Views.Dialogs;

public partial class HookDebugDialog : Window
{
    private readonly IApiServer _apiServer;
    private readonly DispatcherTimer _timer;
    private int _lastCount;

    public HookDebugDialog(IApiServer apiServer)
    {
        _apiServer = apiServer;
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => RefreshLog();
        _timer.Start();

        RefreshLog();
    }

    private void RefreshLog()
    {
        var entries = _apiServer.HookDebugLog;
        var count = entries.Count;

        if (count != _lastCount)
        {
            var selectedIndex = EventList.SelectedIndex;
            var wasAtBottom = selectedIndex == _lastCount - 1 || selectedIndex < 0;

            EventList.ItemsSource = entries.ToList();
            _lastCount = count;

            if (wasAtBottom && count > 0)
            {
                EventList.SelectedIndex = count - 1;
                EventList.ScrollIntoView(entries[count - 1]);
            }
            else if (selectedIndex >= 0 && selectedIndex < count)
            {
                EventList.SelectedIndex = selectedIndex;
            }
        }

        var successCount = entries.Count(e => e.Success);
        var failCount = entries.Count(e => !e.Success);
        var sources = entries.GroupBy(e => e.Source).Select(g => $"{g.Key}: {g.Count()}");
        StatusText.Text = $"{count} events ({successCount} ok, {failCount} failed) — Sources: {string.Join(", ", sources)}";
    }

    private void EventList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (EventList.SelectedItem is HookDebugEntry entry)
        {
            DetailSessionId.Text = entry.SessionId ?? "(no session)";
            DetailStatus.Text = entry.Success ? "OK" : $"FAILED: {entry.Error}";
            DetailStatus.Foreground = entry.Success
                ? new SolidColorBrush(Color.FromRgb(0x50, 0xFA, 0x7B))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55));
            DetailSubscribers.Text = entry.Source == "named-pipe"
                ? "via named-pipe"
                : $"{entry.SubscriberCount} subscriber(s), HTTP {entry.StatusCode}";
            DetailBody.Text = entry.RawBody ?? "(no body — arrived via named pipe)";
        }
        else
        {
            DetailSessionId.Text = "";
            DetailStatus.Text = "";
            DetailSubscribers.Text = "";
            DetailBody.Text = "";
        }
    }

    private void Refresh_Click(object? sender, RoutedEventArgs e) => RefreshLog();
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private void Header_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void AutoRefresh_Changed(object? sender, RoutedEventArgs e)
    {
        if (_timer == null) return;
        if (AutoRefresh?.IsChecked == true)
            _timer.Start();
        else
            _timer.Stop();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer?.Stop();
        base.OnClosed(e);
    }
}
