using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Registers session-tracking services whose interfaces became internal in Phase 3
/// (<c>ISessionActivityService</c>, <c>ILiveSessionTracker</c>). Host apps call
/// this from their <c>ConfigureServices</c>; they then resolve the public
/// <see cref="ISessionLifecycleCoordinator"/> facade rather than the internal
/// interfaces directly.
/// </summary>
public static class CoreSessionServiceRegistration
{
    /// <summary>
    /// Wires the concrete session-tracking services and the
    /// <see cref="ISessionLifecycleCoordinator"/> facade. Assumes the host has
    /// already registered <c>ISessionStateStore</c>, optional <c>IInactivityClock</c>,
    /// <c>ITranscriptWatcher</c>, and the upstream services that
    /// <see cref="LiveSessionTracker"/> and <see cref="SessionActivityService"/> depend on.
    /// </summary>
    public static IServiceCollection AddTerminalHostSessionServices(this IServiceCollection services)
    {
        services.AddSingleton<SessionActivityService>();
        services.AddSingleton<ISessionActivityService>(sp => sp.GetRequiredService<SessionActivityService>());

        services.AddSingleton<LiveSessionTracker>(sp => new LiveSessionTracker(
            sp.GetRequiredService<ISessionStateStore>(),
            sp.GetService<IClaudeSessionIndexService>(),
            sp.GetService<ITranscriptWatcher>(),
            sp.GetRequiredService<SessionActivityService>(),
            sp.GetService<ICollabService>()));
        // Bridge the internal interface to the concrete so consumers inside Core
        // (which still type-against the interface) keep working.
        services.AddSingleton<ILiveSessionTracker>(sp => sp.GetRequiredService<LiveSessionTracker>());

        services.AddSingleton<ISessionLifecycleCoordinator>(sp =>
            new SessionLifecycleCoordinator(
                sp.GetRequiredService<SessionActivityService>(),
                sp.GetRequiredService<LiveSessionTracker>(),
                sp.GetService<IInactivityClock>()));

        services.AddSingleton<ITimelineService>(sp => new TimelineService(
            sp.GetRequiredService<ISessionStateStore>(),
            sp.GetRequiredService<LiveSessionTracker>(),
            sp.GetService<IHookInstaller>()));

        return services;
    }
}
