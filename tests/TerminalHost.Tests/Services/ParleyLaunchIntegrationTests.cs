using System.IO;
using System.Text.Json.Nodes;
using Shouldly;
using TerminalHost.Core.Services;

namespace TerminalHost.Tests.Services;

public class ParleyLaunchIntegrationTests
{
    private static readonly string Home = Path.Combine(Path.GetTempPath(), "parley-home-fake");
    private static readonly string ClaudeJsonPath = Path.Combine(Home, ".claude.json");
    private static readonly string ParleyExe = Path.Combine(Home, ".dotnet", "tools",
        OperatingSystem.IsWindows() ? "parley.exe" : "parley");

    private const string ClaudeJsonWithLegacy = """
        {
          "numStartups": 42,
          "mcpServers": {
            "terminalhost-collab": { "type": "http", "url": "http://127.0.0.1:19280/api/mcp" },
            "other": { "type": "stdio", "command": "other" }
          }
        }
        """;

    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly AppConfiguration _config = new();
    private int _writes;

    private ParleyLaunchIntegration Create(bool parleyInstalled = true)
    {
        if (parleyInstalled) _files[ParleyExe] = "";

        var fs = new Mock<IFileSystem>();
        fs.Setup(f => f.DirectoryExists(It.IsAny<string?>()))
            .Returns<string?>(p => p == Path.Combine(Home, ".claude"));
        fs.Setup(f => f.FileExists(It.IsAny<string?>())).Returns<string?>(p => p != null && _files.ContainsKey(p));
        fs.Setup(f => f.ReadAllText(It.IsAny<string>())).Returns<string>(p => _files[p]);
        fs.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((p, c) => { _files[p] = c; _writes++; });

        var configService = new Mock<IConfigurationService>();
        configService.Setup(c => c.Load(It.IsAny<string>())).Returns(_config);

        return new ParleyLaunchIntegration(configService.Object, fs.Object, Mock.Of<IProcessService>(), Home);
    }

    private JsonObject McpServers() => (JsonNode.Parse(_files[ClaudeJsonPath])!["mcpServers"] as JsonObject)!;

    [Fact]
    public void Enabled_RegistersParley_RemovesLegacyCollab_KeepsEverythingElse()
    {
        _files[ClaudeJsonPath] = ClaudeJsonWithLegacy;
        var sut = Create();

        var launch = sut.PrepareLaunch(Path.Combine("P:", "work", "my-api"));

        var servers = McpServers();
        servers.ContainsKey("terminalhost-collab").ShouldBeFalse();
        servers.ContainsKey("other").ShouldBeTrue();
        servers["parley"]!["type"]!.GetValue<string>().ShouldBe("stdio");
        servers["parley"]!["command"]!.GetValue<string>().ShouldBe("parley");
        servers["parley"]!["args"]!.AsArray().Single()!.GetValue<string>().ShouldBe("mcp");
        JsonNode.Parse(_files[ClaudeJsonPath])!["numStartups"]!.GetValue<int>().ShouldBe(42);

        launch.Environment["PARLEY_SESSION"].ShouldBe("my-api");
        launch.Environment.ContainsKey("PARLEY_URL").ShouldBeFalse();
        launch.PushViaChannels.ShouldBeFalse();
    }

    [Fact]
    public void Disabled_OnlyRemovesLegacyCollab()
    {
        _files[ClaudeJsonPath] = ClaudeJsonWithLegacy;
        _config.Settings.Parley.Enabled = false;
        _config.Settings.Parley.PushViaChannels = true;
        var sut = Create();

        var launch = sut.PrepareLaunch("P:\\work\\my-api");

        McpServers().ContainsKey("terminalhost-collab").ShouldBeFalse();
        McpServers().ContainsKey("parley").ShouldBeFalse();
        launch.Environment.ShouldBeEmpty();
        launch.PushViaChannels.ShouldBeFalse();
    }

    [Fact]
    public void NotInstalled_DoesNotRegisterParley()
    {
        _files[ClaudeJsonPath] = """{ "mcpServers": {} }""";
        var sut = Create(parleyInstalled: false);

        var launch = sut.PrepareLaunch("P:\\work\\my-api");

        _writes.ShouldBe(0);
        launch.Environment.ShouldBeEmpty();
    }

    [Fact]
    public void LegacyEntryNotPointingAtTerminalHost_IsKept()
    {
        _files[ClaudeJsonPath] = """
            { "mcpServers": { "terminalhost-collab": { "type": "http", "url": "http://example.com/mcp" }, "parley": { "command": "parley" } } }
            """;
        var sut = Create();

        sut.PrepareLaunch("P:\\work\\my-api");

        _writes.ShouldBe(0);
    }

    [Fact]
    public void ExistingParleyRegistration_IsLeftAlone()
    {
        _files[ClaudeJsonPath] = """{ "mcpServers": { "parley": { "command": "C:/custom/parley.exe", "args": ["mcp"] } } }""";
        var sut = Create();

        sut.PrepareLaunch("P:\\work\\my-api");

        _writes.ShouldBe(0);
    }

    [Fact]
    public void MalformedClaudeJson_IsNeverOverwritten()
    {
        _files[ClaudeJsonPath] = "{ not json";
        var sut = Create();

        Should.NotThrow(() => sut.PrepareLaunch("P:\\work\\my-api"));

        _files[ClaudeJsonPath].ShouldBe("{ not json");
    }

    [Fact]
    public void PushAndCustomHub_AreReflectedInLaunch()
    {
        _config.Settings.Parley.PushViaChannels = true;
        _config.Settings.Parley.HubUrl = "http://127.0.0.1:20000/";
        var sut = Create();

        var launch = sut.PrepareLaunch("P:\\work\\web\\");

        launch.PushViaChannels.ShouldBeTrue();
        launch.Environment["PARLEY_SESSION"].ShouldBe("web");
        launch.Environment["PARLEY_URL"].ShouldBe("http://127.0.0.1:20000");
    }
}
