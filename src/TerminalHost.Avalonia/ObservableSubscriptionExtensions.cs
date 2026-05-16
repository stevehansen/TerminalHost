using Avalonia.Reactive;

namespace TerminalHost.AvaloniaCompatibility;

internal static class ObservableSubscriptionExtensions
{
    public static IDisposable Subscribe<T>(this IObservable<T> source, Action<T> onNext)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onNext);

        return source.Subscribe(new AnonymousObserver<T>(onNext));
    }
}
