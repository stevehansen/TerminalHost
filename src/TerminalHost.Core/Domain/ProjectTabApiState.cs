namespace TerminalHost.Core.Domain;

/// <summary>
/// A tab's contribution to the REST API state — everything the projector
/// needs to assemble <see cref="ApiRepoInfo"/> / <see cref="ApiRepoDetailInfo"/>
/// for that one tab. The tab owns its own data; the projector owns list-level
/// concerns like Index and IsActive.
/// </summary>
public record ProjectTabApiState(
    string Title,
    string WorkingDirectory,
    string Layout,
    double SplitRatio,
    string ActiveTerminal,
    ApiGitInfo? Git,
    ApiTerminalsInfo Terminals,
    ApiActivityIndicator ActivityIndicator,
    ApiAiAssistantInfo? AiAssistant);
