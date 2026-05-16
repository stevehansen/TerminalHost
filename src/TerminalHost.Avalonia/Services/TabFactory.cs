using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
using TerminalHost.ViewModels;

namespace TerminalHost.Services;

/// <summary>
/// Default <see cref="ITabFactory"/> for the Avalonia host. Resolves the nine
/// service dependencies of <see cref="TerminalPairTabViewModel"/> from
/// <see cref="IServiceProvider"/>. Optional services
/// (<see cref="IClaudeTaskDetectionService"/>, <see cref="ITaskAggregator"/>)
/// are resolved with <c>GetService</c> so they collapse to <c>null</c> when
/// not registered.
/// </summary>
public sealed class TabFactory : ITabFactory
{
    private readonly IServiceProvider _serviceProvider;

    public TabFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public TerminalPairTabViewModel CreateTerminalPairTab(
        TerminalPair pair,
        AiAssistant aiAssistant,
        IReadOnlyList<AiAssistant> enabledAssistants,
        string shellIcon,
        int duplicateIndex)
    {
        return new TerminalPairTabViewModel(
            pair,
            aiAssistant,
            enabledAssistants,
            shellIcon,
            _serviceProvider.GetRequiredService<IStatisticsService>(),
            _serviceProvider.GetRequiredService<ITerminalControlFactory>(),
            _serviceProvider.GetService<IClaudeTaskDetectionService>(),
            _serviceProvider.GetRequiredService<ITimelineService>(),
            _serviceProvider.GetRequiredService<ITaskService>(),
            _serviceProvider.GetService<ITaskAggregator>(),
            _serviceProvider.GetRequiredService<IDispatcherService>(),
            _serviceProvider.GetRequiredService<IGitStatusService>(),
            _serviceProvider.GetRequiredService<IToastService>(),
            duplicateIndex);
    }
}
