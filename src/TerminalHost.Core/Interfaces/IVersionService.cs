namespace TerminalHost.Core.Interfaces;

public interface IVersionService
{
    string InformationalVersion { get; }

    string FullInformationalVersion { get; }
}
