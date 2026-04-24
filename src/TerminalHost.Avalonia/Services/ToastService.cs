using TerminalHost.Core.Interfaces;
using System.Collections.ObjectModel;
using TerminalHost.ViewModels;

namespace TerminalHost.Services;

/// <summary>
/// Service for displaying toast notifications.
/// </summary>
internal sealed class ToastService : IToastService
{
    private const int MaxVisibleToasts = 5;
    private readonly ITimerService _timerService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ISoundService? _soundService;
    private readonly Queue<ToastViewModel> _queue = new();
    private readonly Dictionary<string, IPlatformTimer> _autoCloseTimers = new();
    private readonly object _lock = new();

    public ToastService(ITimerService timerService, IDispatcherService dispatcherService, ISoundService? soundService = null)
    {
        _timerService = timerService;
        _dispatcherService = dispatcherService;
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
                timer.Dispose();
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
            ToastType.Progress => (SoundType?)null,
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
        var timer = _timerService.CreateTimer(toast.AutoCloseDelay, () =>
        {
            RemoveToast(toast.Id);
        });

        _autoCloseTimers[toast.Id] = timer;
        timer.Start();
    }

    private void StopAutoCloseTimer(string id)
    {
        if (_autoCloseTimers.TryGetValue(id, out var timer))
        {
            timer.Stop();
            timer.Dispose();
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

    private void Dispatch(Action action)
    {
        if (_dispatcherService.CheckAccess())
        {
            action();
        }
        else
        {
            // Core IDispatcherService.InvokeAsync takes Func<Task>, not Action
            // Wrap the action in a Func<Task>
            _dispatcherService.InvokeAsync(() => { action(); return Task.CompletedTask; }).Wait();
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

            _service.Dispatch(() =>
            {
                _toast.Message = message;
                _toast.Icon = ToastViewModel.GetDefaultIcon(type);
                _toast.Type = type;
                _toast.AutoClose = true;

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
