namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Platform-specific hook installation into Claude Code's settings.json.
/// Windows uses host.exe --hook commands; macOS uses curl to the REST API.
/// </summary>
public interface IHookInstaller
{
    bool AreHooksInstalled();
    bool InstallHooks();
    bool UninstallHooks();
    void UpgradeHooksIfNeeded();
}
