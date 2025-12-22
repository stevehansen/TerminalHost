using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

public interface IConfigurationService
{
    AppConfiguration Load();
    void Save(AppConfiguration configuration);
    string LoadRawJson();
    (bool success, string? error, string? warning) SaveRawJson(string json);
    // Note: GetConfigFilePath is static in implementation, so not part of interface usually,
    // unless we wrap it. For now, we skip it as it's a helper.
}
