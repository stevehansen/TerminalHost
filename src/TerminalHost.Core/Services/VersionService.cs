using System.Reflection;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

public sealed class VersionService : IVersionService
{
    private readonly Assembly _assembly;

    public VersionService()
        : this(Assembly.GetEntryAssembly() ?? typeof(VersionService).Assembly)
    {
    }

    public VersionService(Assembly assembly)
    {
        _assembly = assembly;
    }

    public string FullInformationalVersion =>
        _assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0";

    public string InformationalVersion
    {
        get
        {
            var raw = FullInformationalVersion;
            var plusIndex = raw.IndexOf('+');
            return plusIndex >= 0 ? raw[..plusIndex] : raw;
        }
    }
}
