using System.Collections.Generic;
using TerminalHost.Core.Domain;
using TerminalHost.Domain;
using TerminalHost.ViewModels;

namespace TerminalHost.Services;

/// <summary>
/// Constructs <see cref="TerminalPairTabViewModel"/> instances with all required
/// services resolved from DI. Hides the long argument list (the Avalonia VM ctor
/// takes nine service dependencies in addition to per-tab values) so callers — and
/// the eventual workspace lifecycle service — can build tabs without holding those
/// service references inline.
/// </summary>
/// <remarks>
/// Step 4e (#48): introduced to remove the inline <c>new TerminalPairTabViewModel(...)</c>
/// call from <c>MainViewModel.OpenProjectTabCore</c>. Wiring (event subscriptions,
/// explorer init, panel restore) stays in the caller because those touch
/// <c>MainViewModel</c>-private state. The Avalonia VM creates its terminals
/// lazily on first selection, so the factory does not receive controls (unlike
/// the WPF version).
/// </remarks>
public interface ITabFactory
{
    /// <summary>
    /// Creates a tab view model for the given pair. Terminals are created lazily
    /// by the VM itself on first selection. The caller is responsible for event
    /// hookup, explorer init, and panel restore.
    /// </summary>
    TerminalPairTabViewModel CreateTerminalPairTab(
        TerminalPair pair,
        AiAssistant aiAssistant,
        IReadOnlyList<AiAssistant> enabledAssistants,
        string shellIcon,
        int duplicateIndex);

    /// <summary>
    /// Creates a <see cref="FileExplorerViewModel"/> with the eight service
    /// dependencies resolved from DI. <paramref name="rootPath"/> is stamped
    /// onto <see cref="FileExplorerViewModel.RootPath"/> so the caller's first
    /// <c>InitializeAsync</c> call can read it back; the explorer's actual
    /// tree scan is still triggered separately.
    /// </summary>
    FileExplorerViewModel CreateFileExplorer(string rootPath);
}
