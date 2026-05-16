using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
using TerminalHost.ViewModels;

namespace TerminalHost.Services;

/// <summary>
/// Default <see cref="ITabFactory"/> for the WPF host. Resolves the four
/// service dependencies of <see cref="TerminalPairTabViewModel"/> from
/// <see cref="IServiceProvider"/>. <see cref="ITaskService"/> is optional —
/// the VM treats <c>null</c> as "task panel disabled".
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
            _serviceProvider.GetRequiredService<IGitStatusService>(),
            _serviceProvider.GetRequiredService<IToastService>(),
            duplicateIndex,
            _serviceProvider.GetService<ITaskService>());
    }
}
