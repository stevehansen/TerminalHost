using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.Windows.Services;

/// <summary>
/// Service for displaying toast notifications.
/// </summary>
public sealed class ToastService : IToastService
{
    private const int MaxVisibleToasts = 5;
    private readonly Queue<ToastViewModel> _queue = new();
    private readonly Dictionary<string, DispatcherTimer> _autoCloseTimers = new();
    private readonly object _lock = new();
    private readonly ISoundService? _soundService;

    public ToastService() { }

    public ToastService(ISoundService soundService)
    {
        _soundService = soundService;
    }

    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    public string Show(string message, ToastType type = ToastType.Info, bool autoClose = true)
    {
        return Show(message, ToastViewModel.GetDefaultIcon(type), type, autoClose);
    }

    public string Show(string message, string icon, bool autoClose = true)
    {
        return Show(message, icon, ToastType.Info, autoClose);
    }

    private string Show(string message, string icon, ToastType type, bool autoClose)
    {
        var toast = new ToastViewModel
        {
            Id = Guid.NewGuid().ToString(),
            Message = message,
            Icon = icon,
            Type = type,
            AutoClose = autoClose,
            IsCloseable = true
        };

        toast.CloseRequested += OnToastCloseRequested;

        Dispatch(() => AddToast(toast));

        return toast.Id;
    }

    public IProgressToast ShowProgress(string message)
    {
        var toast = new ToastViewModel
        {
            Id = Guid.NewGuid().ToString(),
            Message = message,
            Icon = ToastViewModel.GetDefaultIcon(ToastType.Progress),
            Type = ToastType.Progress,
            AutoClose = false,
            IsCloseable = true
        };

        toast.CloseRequested += OnToastCloseRequested;

        Dispatch(() => AddToast(toast));

        return new ProgressToast(this, toast);
    }

    public void Update(string id, string message, string? icon = null)
    {
        Dispatch(() =>
        {
            var toast = FindToast(id);
            if (toast != null)
            {
                toast.Message = message;
                if (icon != null)
                {
                    toast.Icon = icon;
                }
            }
        });
    }

    public void Close(string id)
    {
        Dispatch(() => RemoveToast(id));
    }

    public void CloseAll()
    {
        Dispatch(() =>
        {
            foreach (var timer in _autoCloseTimers.Values)
            {
                timer.Stop();
            }
            _autoCloseTimers.Clear();

            foreach (var toast in Toasts.ToList())
            {
                toast.CloseRequested -= OnToastCloseRequested;
            }
            Toasts.Clear();

            lock (_lock)
            {
                _queue.Clear();
            }
        });
    }

    private void AddToast(ToastViewModel toast)
    {
        if (Toasts.Count >= MaxVisibleToasts)
        {
            lock (_lock)
            {
                _queue.Enqueue(toast);
            }
            return;
        }

        Toasts.Add(toast);

        // Play sound based on toast type
        PlaySoundForToast(toast.Type);

        if (toast.AutoClose)
        {
            StartAutoCloseTimer(toast);
        }
    }

    private void PlaySoundForToast(ToastType type)
    {
        if (_soundService == null) return;

        var soundType = type switch
        {
            ToastType.Success => SoundType.Success,
            ToastType.Error => SoundType.Error,
            ToastType.Warning => SoundType.Warning,
            ToastType.Info => SoundType.Info,
            ToastType.Progress => (SoundType?)null, // No sound for progress start
            _ => null
        };

        if (soundType.HasValue)
        {
            _soundService.Play(soundType.Value);
        }
    }

    private void RemoveToast(string id)
    {
        var toast = FindToast(id);
        if (toast == null) return;

        StopAutoCloseTimer(id);
        toast.CloseRequested -= OnToastCloseRequested;
        Toasts.Remove(toast);

        ShowNextFromQueue();
    }

    private void ShowNextFromQueue()
    {
        ToastViewModel? nextToast = null;

        lock (_lock)
        {
            if (_queue.Count > 0)
            {
                nextToast = _queue.Dequeue();
            }
        }

        if (nextToast != null)
        {
            Toasts.Add(nextToast);

            if (nextToast.AutoClose)
            {
                StartAutoCloseTimer(nextToast);
            }
        }
    }

    private ToastViewModel? FindToast(string id)
    {
        return Toasts.FirstOrDefault(t => t.Id == id);
    }

    private void StartAutoCloseTimer(ToastViewModel toast)
    {
        var timer = new DispatcherTimer
        {
            Interval = toast.AutoCloseDelay
        };

        timer.Tick += (s, e) =>
        {
            timer.Stop();
            RemoveToast(toast.Id);
        };

        _autoCloseTimers[toast.Id] = timer;
        timer.Start();
    }

    private void StopAutoCloseTimer(string id)
    {
        if (_autoCloseTimers.TryGetValue(id, out var timer))
        {
            timer.Stop();
            _autoCloseTimers.Remove(id);
        }
    }

    private void OnToastCloseRequested(object? sender, EventArgs e)
    {
        if (sender is ToastViewModel toast)
        {
            Close(toast.Id);
        }
    }

    private static void Dispatch(Action action)
    {
        if (System.Windows.Application.Current?.Dispatcher is Dispatcher dispatcher)
        {
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.Invoke(action);
            }
        }
        else
        {
            // No dispatcher available (e.g., in tests), just run directly
            action();
        }
    }

    /// <summary>
    /// Internal implementation of IProgressToast.
    /// </summary>
    private sealed class ProgressToast : IProgressToast
    {
        private readonly ToastService _service;
        private readonly ToastViewModel _toast;
        private bool _disposed;

        public string Id => _toast.Id;

        public ProgressToast(ToastService service, ToastViewModel toast)
        {
            _service = service;
            _toast = toast;
        }

        public void Update(string message, string? icon = null)
        {
            if (_disposed) return;
            _service.Update(_toast.Id, message, icon);
        }

        public void Complete(string message, ToastType type = ToastType.Success)
        {
            if (_disposed) return;

            Dispatch(() =>
            {
                _toast.Message = message;
                _toast.Icon = ToastViewModel.GetDefaultIcon(type);
                _toast.Type = type;
                _toast.AutoClose = true;

                // Play completion sound
                _service.PlaySoundForToast(type);

                _service.StartAutoCloseTimer(_toast);
            });
        }

        public void Fail(string message)
        {
            Complete(message, ToastType.Error);
        }

        public void Close()
        {
            if (_disposed) return;
            _service.Close(_toast.Id);
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
