using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Workspace;

public sealed class CommandPalette : ICommandPalette
{
    private readonly IReadOnlyList<ICommandProvider> _providers;
    private readonly ICommandContext _context;
    private readonly List<PaletteCommand> _additional = new();

    public CommandPalette(IEnumerable<ICommandProvider> providers, ICommandContext context)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToList();
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public IReadOnlyList<PaletteCommand> Commands
    {
        get
        {
            var list = new List<PaletteCommand>();
            foreach (var provider in _providers)
                list.AddRange(provider.GetCommands(_context));
            list.AddRange(_additional);
            return list;
        }
    }

    public IReadOnlyList<PaletteCommand> Filter(string query)
    {
        var q = query?.ToLower() ?? "";
        return Commands
            .Where(c => c.CanExecute == null || c.CanExecute())
            .Where(c =>
                string.IsNullOrEmpty(q) ||
                c.Name.ToLower().Contains(q) ||
                (c.Description?.ToLower().Contains(q) ?? false) ||
                c.Category.ToLower().Contains(q))
            .ToList();
    }

    public Task InvokeAsync(PaletteCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Execute();
        return Task.CompletedTask;
    }

    public IDisposable Register(PaletteCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _additional.Add(command);
        return new Registration(this, command);
    }

    private sealed class Registration : IDisposable
    {
        private readonly CommandPalette _owner;
        private readonly PaletteCommand _command;
        private bool _disposed;

        public Registration(CommandPalette owner, PaletteCommand command)
        {
            _owner = owner;
            _command = command;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner._additional.Remove(_command);
        }
    }
}
