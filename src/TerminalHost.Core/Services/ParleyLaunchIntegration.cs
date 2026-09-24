using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// What an AI session launched by TerminalHost needs in order to use Parley.
/// </summary>
/// <param name="Environment">Variables to set for the AI process (e.g. PARLEY_SESSION); empty when Parley is off.</param>
/// <param name="PushViaChannels">Whether to add <see cref="ParleyLaunchIntegration.ChannelEntry"/> to Claude Code's
/// <c>--dangerously-load-development-channels</c> flag.</param>
public sealed record ParleyLaunch(IReadOnlyDictionary<string, string> Environment, bool PushViaChannels);

/// <summary>
/// Prepares AI sessions for Parley: registers the <c>parley</c> stdio MCP server with Claude Code
/// (user-scope <c>~/.claude.json</c>) and Codex, removes the obsolete <c>terminalhost-collab</c>
/// registration (TerminalHost's /api/mcp no longer exists), and names the session after the tab.
/// Registration only happens when Parley is enabled and the <c>parley</c> tool is installed, so
/// users without Parley never get a broken MCP entry. Best-effort: never throws.
/// </summary>
public sealed class ParleyLaunchIntegration
{
    public const string ServerName = "parley";
    public const string ChannelEntry = "server:parley";
    private const string LegacyCollabServerName = "terminalhost-collab";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IConfigurationService _configService;
    private readonly IFileSystem _fileSystem;
    private readonly IProcessService _processService;
    private readonly string _homeDir;
    private int _codexChecked;

    public ParleyLaunchIntegration(IConfigurationService configService, IFileSystem fileSystem, IProcessService processService)
        : this(configService, fileSystem, processService, System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile))
    {
    }

    internal ParleyLaunchIntegration(IConfigurationService configService, IFileSystem fileSystem, IProcessService processService, string homeDir)
    {
        _configService = configService;
        _fileSystem = fileSystem;
        _processService = processService;
        _homeDir = homeDir;
    }

    /// <summary>
    /// Call before launching a (non-shell) AI command in <paramref name="workingDir"/>.
    /// </summary>
    public ParleyLaunch PrepareLaunch(string workingDir)
    {
        var settings = _configService.Load().Settings.Parley ?? new ParleySettings();
        var active = settings.Enabled && IsParleyInstalled();

        EnsureClaudeRegistration(register: active);
        EnsureCodexRegistration(register: active);

        if (!active)
            return new ParleyLaunch(new Dictionary<string, string>(), false);

        var env = new Dictionary<string, string>();
        var sessionName = Path.GetFileName(workingDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrEmpty(sessionName))
            env["PARLEY_SESSION"] = sessionName; // same as the tab title
        if (!string.IsNullOrWhiteSpace(settings.HubUrl) &&
            !string.Equals(settings.HubUrl.TrimEnd('/'), ParleySettings.DefaultHubUrl, StringComparison.OrdinalIgnoreCase))
            env["PARLEY_URL"] = settings.HubUrl.TrimEnd('/');

        return new ParleyLaunch(env, settings.PushViaChannels);
    }

    /// <summary>Whether the <c>parley</c> command is on PATH or in the dotnet global tools folder.</summary>
    private bool IsParleyInstalled()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "parley.exe", "parley.cmd" } : new[] { "parley" };
        var dirs = (System.Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(Path.Combine(_homeDir, ".dotnet", "tools"));
        return dirs.Any(dir => names.Any(name => _fileSystem.FileExists(Path.Combine(dir, name))));
    }

    /// <summary>Edits ~/.claude.json mcpServers, writing only when something changed.</summary>
    private void EnsureClaudeRegistration(bool register)
    {
        try
        {
            if (!_fileSystem.DirectoryExists(Path.Combine(_homeDir, ".claude")))
                return; // Claude Code not installed

            var path = Path.Combine(_homeDir, ".claude.json");
            var root = _fileSystem.FileExists(path)
                ? JsonNode.Parse(_fileSystem.ReadAllText(path)) as JsonObject
                : new JsonObject();
            if (root == null) return; // unexpected shape: never clobber the user's config

            var servers = root["mcpServers"] as JsonObject;
            var changed = false;

            if (servers?[LegacyCollabServerName] is JsonObject legacy && PointsAtTerminalHostMcp(legacy))
            {
                servers.Remove(LegacyCollabServerName);
                changed = true;
            }

            if (register && servers?.ContainsKey(ServerName) != true)
            {
                if (servers == null)
                {
                    servers = new JsonObject();
                    root["mcpServers"] = servers;
                }
                servers[ServerName] = new JsonObject
                {
                    ["type"] = "stdio",
                    ["command"] = "parley",
                    ["args"] = new JsonArray("mcp"),
                };
                changed = true;
            }

            if (changed)
                _fileSystem.WriteAllText(path, root.ToJsonString(WriteOptions));
        }
        catch
        {
            // Best-effort: a malformed or locked ~/.claude.json must not block the launch
        }
    }

    private static bool PointsAtTerminalHostMcp(JsonObject entry) =>
        entry["url"] is JsonValue value && value.TryGetValue<string>(out var url) &&
        url.Contains("/api/mcp", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Uses the Codex CLI (which owns ~/.codex/config.toml) to swap terminalhost-collab for parley.
    /// Once per app session, fire-and-forget.
    /// </summary>
    private void EnsureCodexRegistration(bool register)
    {
        if (Interlocked.Exchange(ref _codexChecked, 1) == 1)
            return;
        if (!_fileSystem.DirectoryExists(Path.Combine(_homeDir, ".codex")))
            return; // Codex CLI not installed

        _ = Task.Run(async () =>
        {
            try
            {
                var (exit, output, _) = await _processService.RunAsync("codex", "mcp list", timeout: TimeSpan.FromSeconds(10));
                if (exit != 0) return;

                if (ListsServer(output, LegacyCollabServerName))
                    await _processService.RunAsync("codex", $"mcp remove {LegacyCollabServerName}", timeout: TimeSpan.FromSeconds(10));

                if (register && !ListsServer(output, ServerName))
                    await _processService.RunAsync("codex", $"mcp add {ServerName} -- parley mcp", timeout: TimeSpan.FromSeconds(10));
            }
            catch
            {
                // Best-effort: codex missing from PATH etc. must not disrupt the terminal launch
            }
        });
    }

    private static bool ListsServer(string codexMcpListOutput, string name) =>
        Regex.IsMatch(codexMcpListOutput, $@"(?m)^\s*{Regex.Escape(name)}(\s|$)");
}
