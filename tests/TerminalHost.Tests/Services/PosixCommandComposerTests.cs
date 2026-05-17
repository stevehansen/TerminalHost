using System;
using System.Collections.Generic;
using System.IO;
using Shouldly;
using TerminalHost.Posix.Services;

namespace TerminalHost.Tests.Services;

public class PosixCommandComposerTests
{
    private readonly PosixCommandComposer _composer = new();

    // WithEnvironment

    [Fact]
    public void WithEnvironment_EmptyDict_ReturnsCommandUnchanged()
    {
        _composer.WithEnvironment("ls", new Dictionary<string, string>()).ShouldBe("ls");
    }

    [Fact]
    public void WithEnvironment_OneVar_PrefixesVarBeforeCommand()
    {
        var env = new Dictionary<string, string> { ["K"] = "V" };

        _composer.WithEnvironment("cmd", env).ShouldBe("K=V cmd");
    }

    [Fact]
    public void WithEnvironment_TwoVars_PreservesInsertionOrder()
    {
        // Dictionary<TKey,TValue> preserves insertion order in .NET 8 in practice;
        // the composer joins keys via env.Select, so the produced order matches insertion order.
        var env = new Dictionary<string, string>
        {
            ["K1"] = "V1",
            ["K2"] = "V2",
        };

        _composer.WithEnvironment("cmd", env).ShouldBe("K1=V1 K2=V2 cmd");
    }

    // WithWorkingDirectory

    [Fact]
    public void WithWorkingDirectory_EmptyDir_ReturnsCommandUnchanged()
    {
        _composer.WithWorkingDirectory("ls", "").ShouldBe("ls");
    }

    [Fact]
    public void WithWorkingDirectory_AnyDir_ReturnsCommandUnchanged()
    {
        // POSIX PTYs cd natively — this is explicit non-behavior.
        _composer.WithWorkingDirectory("ls", "/tmp").ShouldBe("ls");
        _composer.WithWorkingDirectory("claude", "/home/user/proj").ShouldBe("claude");
    }

    // IsBuiltInShell

    [Theory]
    [InlineData("zsh")]
    [InlineData("bash")]
    [InlineData("sh")]
    [InlineData("fish")]
    [InlineData("/bin/zsh")]
    public void IsBuiltInShell_KnownShells_ReturnsTrue(string exe)
    {
        _composer.IsBuiltInShell(exe).ShouldBeTrue();
    }

    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("")]
    [InlineData("claude")]
    [InlineData("pwsh")]      // Was the EndsWith("sh") overmatch — now correctly rejected.
    [InlineData("dash")]      // Same family of false positives.
    [InlineData("xonsh")]
    [InlineData("fresh")]     // Non-shell command ending in "sh".
    public void IsBuiltInShell_NonPosixShells_ReturnsFalse(string exe)
    {
        _composer.IsBuiltInShell(exe).ShouldBeFalse();
    }

    // DefaultShell

    [Fact]
    public void DefaultShell_ReturnsPosixShellPath()
    {
        var shell = _composer.DefaultShell;

        shell.ShouldNotBeNullOrEmpty();
        var looksPosix = shell.StartsWith("/", StringComparison.Ordinal)
            || shell.Contains("zsh", StringComparison.OrdinalIgnoreCase)
            || shell.Contains("bash", StringComparison.OrdinalIgnoreCase)
            || shell.Contains("sh", StringComparison.OrdinalIgnoreCase);
        looksPosix.ShouldBeTrue($"Expected POSIX-shell-looking path, got: {shell}");
    }

    // TryResolveExecutable

    [Fact]
    public void TryResolveExecutable_EmptyString_ReturnsFalse()
    {
        var ok = _composer.TryResolveExecutable("", out var path);

        ok.ShouldBeFalse();
        path.ShouldBe("");
    }

    [Fact]
    public void TryResolveExecutable_AbsoluteExistingPath_ReturnsTrue()
    {
        var temp = Path.GetTempFileName();
        try
        {
            var ok = _composer.TryResolveExecutable(temp, out var path);

            ok.ShouldBeTrue();
            path.ShouldBe(temp);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void TryResolveExecutable_RandomNonexistentCommand_ReturnsFalse()
    {
        var nonexistent = "tho-" + Guid.NewGuid().ToString("N");

        var ok = _composer.TryResolveExecutable(nonexistent, out var path);

        ok.ShouldBeFalse();
        path.ShouldBe("");
    }
}
