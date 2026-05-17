using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

public static class EventAggregatorExtensions
{
    /// <summary>
    /// Publishes an <see cref="ApiEvent"/>. No-op when the aggregator is null
    /// (callers don't need to null-check at every call site).
    /// </summary>
    public static void Publish(this IEventAggregatorService? aggregator, string type, int? repoIndex = null, object? data = null)
    {
        aggregator?.Publish(new ApiEvent
        {
            Type = type,
            RepoIndex = repoIndex,
            Data = data
        });
    }
}
