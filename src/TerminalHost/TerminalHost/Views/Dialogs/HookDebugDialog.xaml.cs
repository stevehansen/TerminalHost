using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
            // Preserve selection
            var selectedIndex = EventList.SelectedIndex;
            var wasAtBottom = selectedIndex == _lastCount - 1 || selectedIndex < 0;

            EventList.ItemsSource = entries.ToList();
            _lastCount = count;

            if (wasAtBottom && count > 0)
            {
                EventList.SelectedIndex = count - 1;
                EventList.ScrollIntoView(EventList.Items[count - 1]);
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

    private void EventList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (EventList.SelectedItem is HookDebugEntry entry)
        {
            DetailSessionId.Text = entry.SessionId ?? "(no session)";
            DetailStatus.Text = entry.Success ? "OK" : $"FAILED: {entry.Error}";
            DetailStatus.Foreground = entry.Success
                ? new SolidColorBrush(Color.FromRgb(0x50, 0xFA, 0x7B))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55));
            DetailSubscribers.Text = entry.Source == "named-pipe" ? "via named-pipe" : $"{entry.SubscriberCount} subscriber(s), HTTP {entry.StatusCode}";
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

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshLog();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void AutoRefresh_Changed(object sender, RoutedEventArgs e)
    {
        if (_timer == null) return; // Called during InitializeComponent before timer exists
        if (AutoRefresh?.IsChecked == true)
            _timer.Start();
        else
            _timer.Stop();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer?.Stop();
        base.OnClosed(e);
    }
}

public class BoolToSuccessColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true
            ? new SolidColorBrush(Color.FromRgb(0x50, 0xFA, 0x7B))
            : new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToStringConverter : IValueConverter
{
    public string TrueValue { get; set; } = "True";
    public string FalseValue { get; set; } = "False";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? TrueValue : FalseValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
