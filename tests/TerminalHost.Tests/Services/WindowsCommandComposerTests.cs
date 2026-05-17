using System;
using System.Collections.Generic;
using System.IO;
using Shouldly;
using TerminalHost.Core.Services;

namespace TerminalHost.Tests.Services;

public class WindowsCommandComposerTests
{
    private readonly WindowsCommandComposer _composer = new();

    // WithEnvironment

    [Fact]
    public void WithEnvironment_EmptyDict_ReturnsCommandUnchanged()
    {
        _composer.WithEnvironment("cmd", new Dictionary<string, string>()).ShouldBe("cmd");
    }

    [Fact]
    public void WithEnvironment_OneVar_PrefixesSetBeforeCommand()
    {
        var env = new Dictionary<string, string> { ["K"] = "V" };

        _composer.WithEnvironment("cmd", env).ShouldBe("set \"K=V\" && cmd");
    }

    [Fact]
    public void WithEnvironment_TwoVars_PreservesInsertionOrder()
    {
        // The interface XML doc promises iteration order is preserved as the caller's
        // dictionary iteration order. Dictionary<,> uses insertion order.
        var env = new Dictionary<string, string>
        {
            ["K1"] = "V1",
            ["K2"] = "V2",
        };

        _composer.WithEnvironment("cmd", env).ShouldBe("set \"K1=V1\" && set \"K2=V2\" && cmd");
    }

    [Fact]
    public void WithEnvironment_ValueWithDoubleQuote_EscapesByDoubling()
    {
        var env = new Dictionary<string, string> { ["K"] = "a\"b" };

        // The embedded quote must be doubled so cmd doesn't terminate the set value early.
        _composer.WithEnvironment("cmd", env).ShouldBe("set \"K=a\"\"b\" && cmd");
    }

    [Fact]
    public void WithEnvironment_ValueWithPercent_EscapesByDoubling()
    {
        var env = new Dictionary<string, string> { ["K"] = "50%off" };

        // % triggers cmd variable expansion; must be doubled to keep the value literal.
        _composer.WithEnvironment("cmd", env).ShouldBe("set \"K=50%%off\" && cmd");
    }

    // WithWorkingDirectory

    [Fact]
    public void WithWorkingDirectory_EmptyDir_ReturnsCommandUnchanged()
    {
        _composer.WithWorkingDirectory("cmd.exe", "").ShouldBe("cmd.exe");
    }

    [Fact]
    public void WithWorkingDirectory_WhitespaceDir_ReturnsCommandUnchanged()
    {
        _composer.WithWorkingDirectory("cmd.exe", "   ").ShouldBe("cmd.exe");
    }

    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("cmd")]
    public void WithWorkingDirectory_BareCmd_WrapsWithCdSlashD(string head)
    {
        var result = _composer.WithWorkingDirectory(head, @"C:\proj");

        result.ShouldBe("cmd.exe /K cd /d \"C:\\proj\"");
    }

    [Fact]
    public void WithWorkingDirectory_CmdWithArgs_PreservesArgsAfterChain()
    {
        // Regression: previously the cmd branch returned 'cmd.exe /K cd /d "dir" ' and dropped '/c something'.
        var result = _composer.WithWorkingDirectory("cmd.exe /c echo hi", @"C:\proj");

        result.ShouldBe("cmd.exe /K cd /d \"C:\\proj\" && cmd.exe /c echo hi");
    }

    [Theory]
    [InlineData("pwsh.exe")]
    [InlineData("pwsh")]
    [InlineData("powershell.exe")]
    [InlineData("powershell")]
    public void WithWorkingDirectory_PowerShellVariants_AppendsWorkingDirectoryFlag(string head)
    {
        var result = _composer.WithWorkingDirectory(head, @"C:\proj");

        result.ShouldBe($"{head} -NoExit -WorkingDirectory \"C:\\proj\"");
    }

    [Fact]
    public void WithWorkingDirectory_PwshWithArgs_InjectsFlagsBeforeUserArgs()
    {
        // The -NoExit -WorkingDirectory flags must come immediately after the executable so
        // pwsh's parameter binder sees them; appending after user args is brittle.
        var result = _composer.WithWorkingDirectory("pwsh.exe -NoProfile -Command Get-Date", @"C:\proj");

        result.ShouldBe("pwsh.exe -NoExit -WorkingDirectory \"C:\\proj\" -NoProfile -Command Get-Date");
    }

    [Fact]
    public void WithWorkingDirectory_FullPathPwsh_RecognizedViaPathGetFileName()
    {
        var head = @"C:\Tools\pwsh.exe";

        var result = _composer.WithWorkingDirectory(head, @"C:\proj");

        result.ShouldBe($"{head} -NoExit -WorkingDirectory \"C:\\proj\"");
    }

    [Fact]
    public void WithWorkingDirectory_QuotedFullPathPwsh_RecognizedAndUnquoted()
    {
        // Regression: '"C:\Program Files\PowerShell\7\pwsh.exe"'.Split(' ')[0] = '"C:\Program'
        // which used to miss the pwsh branch. The quote-aware splitter keeps the head intact.
        var head = "\"C:\\Program Files\\PowerShell\\7\\pwsh.exe\"";

        var result = _composer.WithWorkingDirectory(head, @"C:\proj");

        result.ShouldBe($"{head} -NoExit -WorkingDirectory \"C:\\proj\"");
    }

    [Fact]
    public void WithWorkingDirectory_OtherCommand_WrappedWithCmdCdAndChain()
    {
        var result = _composer.WithWorkingDirectory("claude.exe", @"C:\proj");

        result.ShouldBe("cmd.exe /K cd /d \"C:\\proj\" && claude.exe");
    }

    // IsBuiltInShell

    [Theory]
    [InlineData("cmd")]
    [InlineData("cmd.exe")]
    [InlineData("pwsh")]
    [InlineData("pwsh.exe")]
    [InlineData("powershell")]
    [InlineData("bash.exe")]
    [InlineData("wsl.exe")]
    public void IsBuiltInShell_KnownShells_ReturnsTrue(string exe)
    {
        _composer.IsBuiltInShell(exe).ShouldBeTrue();
    }

    [Theory]
    [InlineData("zsh")]
    [InlineData("fish")]
    [InlineData("")]
    [InlineData("claude.exe")]
    public void IsBuiltInShell_UnknownOrNonWindows_ReturnsFalse(string exe)
    {
        _composer.IsBuiltInShell(exe).ShouldBeFalse();
    }

    [Fact]
    public void IsBuiltInShell_FullPath_RecognizedViaPathGetFileName()
    {
        _composer.IsBuiltInShell(@"C:\Windows\System32\cmd.exe").ShouldBeTrue();
    }

    // DefaultShell

    [Fact]
    public void DefaultShell_ReturnsExeName()
    {
        var shell = _composer.DefaultShell;

        shell.ShouldNotBeNullOrEmpty();
        shell.EndsWith(".exe", StringComparison.OrdinalIgnoreCase).ShouldBeTrue($"Expected .exe suffix, got: {shell}");
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
        var nonexistent = "thw-" + Guid.NewGuid().ToString("N");

        var ok = _composer.TryResolveExecutable(nonexistent, out var path);

        ok.ShouldBeFalse();
        path.ShouldBe("");
    }
}
