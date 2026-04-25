using System;
using System.Collections.Generic;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Single canonical view of <see cref="FocusTask"/>s across every backing source
/// (manual <see cref="ITaskService"/>, persisted <see cref="IClaudeTaskFileService"/>,
/// terminal-output <see cref="IClaudeTaskDetectionService"/>).
/// Owns dedup identity, source priority, workspace filtering, and change notification
/// so callers stop re-implementing the merge inline with diverging semantics.
/// </summary>
public interface ITaskAggregator
{
    /// <summary>
    /// All tasks across every source, deduplicated by canonical identity.
    /// Source priority on collision: manual &gt; persisted file &gt; terminal detection.
    /// </summary>
    IReadOnlyList<FocusTask> GetAll();

    /// <summary>
    /// Tasks visible for the given workspace path. Manual tasks require an
    /// explicit project-path match; Claude-derived tasks (file or detection)
    /// are also included when they have no project paths recorded.
    /// </summary>
    IReadOnlyList<FocusTask> GetForWorkspace(string workspacePath);

    /// <summary>
    /// Raised when any backing source signals a change. Pass-through:
    /// fires once per source event without coalescing.
    /// </summary>
    event EventHandler? Changed;
}
