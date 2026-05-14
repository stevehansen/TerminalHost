using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Workspace;

/// <summary>
/// Aggregates palette commands from one or more <see cref="ICommandProvider"/>s
/// plus runtime registrations, and exposes filtered views + a single dispatch
/// point so MainViewModel no longer hand-rolls the 1,000+ LOC registration block.
/// </summary>
public interface ICommandPalette
{
    /// <summary>
    /// All commands from every registered provider plus runtime registrations.
    /// Recomputed on access — providers are expected to return stable lists.
    /// </summary>
    IReadOnlyList<PaletteCommand> Commands { get; }

    /// <summary>
    /// Returns commands whose <c>CanExecute</c> passes and whose
    /// <c>Name</c>, <c>Description</c>, or <c>Category</c> contains
    /// <paramref name="query"/> (case-insensitive). Empty/null query
    /// returns all <c>CanExecute</c>-passing commands.
    /// </summary>
    IReadOnlyList<PaletteCommand> Filter(string query);

    /// <summary>
    /// Invokes <see cref="PaletteCommand.Execute"/> and returns a completed task.
    /// <paramref name="ct"/> is currently unused (no async commands today) but
    /// is part of the interface so future async commands don't break callers.
    /// </summary>
    Task InvokeAsync(PaletteCommand command, CancellationToken ct = default);

    /// <summary>
    /// Adds <paramref name="command"/> to the runtime command set. Disposing
    /// the returned handle removes it.
    /// </summary>
    IDisposable Register(PaletteCommand command);
}
