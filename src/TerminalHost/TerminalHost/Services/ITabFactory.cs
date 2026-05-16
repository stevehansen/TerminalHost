using System.Collections.Generic;
using TerminalHost.Core.Domain;
using TerminalHost.Domain;
using TerminalHost.ViewModels;

namespace TerminalHost.Services;

/// <summary>
/// Constructs <see cref="TerminalPairTabViewModel"/> instances with all required
/// services resolved from DI. Hides the long argument list (the WPF VM ctor takes
/// four service dependencies in addition to per-tab values) so callers — and the
/// eventual workspace lifecycle service — can build tabs without holding those
/// service references inline.
/// </summary>
/// <remarks>
/// Step 4e (#48): introduced to remove the inline <c>new TerminalPairTabViewModel(...)</c>
/// call from <c>MainViewModel.OpenProjectTabCore</c>. Wiring (event subscriptions,
/// terminal controls, container state, panel restore) stays in the caller because
/// those touch <c>MainViewModel</c>-private state.
/// </remarks>
public interface ITabFactory
{
    /// <summary>
    /// Creates a tab view model for the given pair. The returned VM has no
    /// terminal controls attached and no events wired — the caller is
    /// responsible for <c>SetTerminalControls</c>, panel init, and event hookup.
    /// </summary>
    TerminalPairTabViewModel CreateTerminalPairTab(
        TerminalPair pair,
        AiAssistant aiAssistant,
        IReadOnlyList<AiAssistant> enabledAssistants,
        string shellIcon,
        int duplicateIndex);
}
