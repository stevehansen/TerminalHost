using System.IO;
using System.Threading.Tasks;
using Moq;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;

namespace TerminalHost.Tests.Services;

public class GitWorkspaceFactoryTests
{
    private readonly Mock<IGitStatusService> _status = new();
    private readonly Mock<IGitHubService> _gitHub = new();
    private readonly Mock<IGitPrService> _gitPr = new();
    private readonly Mock<IGitWorktreeService> _worktrees = new();
    private readonly GitWorkspaceFactory _factory;

    public GitWorkspaceFactoryTests()
    {
        _factory = new GitWorkspaceFactory(_status.Object, _gitHub.Object, _gitPr.Object, _worktrees.Object);
    }

    private void SetupRepo(bool isRepo = true)
    {
        _status.Setup(s => s.GetGitStatusAsync(It.IsAny<string>()))
            .ReturnsAsync(new GitStatus { IsGitRepository = isRepo, BranchName = "main" });
    }

    [Fact]
    public async Task OpenAsync_WhenNotInRepo_ReturnsNull()
    {
        SetupRepo(isRepo: false);

        var ws = await _factory.OpenAsync(Path.GetTempPath());

        ws.ShouldBeNull();
    }

    [Fact]
    public async Task OpenAsync_WhenInRepo_ReturnsNonNull()
    {
        SetupRepo();

        var ws = await _factory.OpenAsync(Path.GetTempPath());

        ws.ShouldNotBeNull();
    }

    [Fact]
    public async Task OpenAsync_CalledTwiceForSamePath_ReturnsSameInstance()
    {
        SetupRepo();
        var path = Path.GetTempPath();

        var first = await _factory.OpenAsync(path);
        var second = await _factory.OpenAsync(path);

        first.ShouldNotBeNull();
        second.ShouldBeSameAs(first);
        // Cache hit on the second call: status probe should only have run once.
        _status.Verify(s => s.GetGitStatusAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task OpenAsync_TrailingSlashAndNoTrailingSlash_ReturnSameInstance()
    {
        SetupRepo();
        var basePath = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var withSlash = basePath + Path.DirectorySeparatorChar;

        var a = await _factory.OpenAsync(basePath);
        var b = await _factory.OpenAsync(withSlash);

        a.ShouldNotBeNull();
        b.ShouldBeSameAs(a);
    }

    [Fact]
    public async Task OpenAsync_CacheKeyIsCaseInsensitive()
    {
        SetupRepo();
        var upper = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var lower = upper.ToLowerInvariant();

        var a = await _factory.OpenAsync(upper);
        var b = await _factory.OpenAsync(lower);

        a.ShouldNotBeNull();
        b.ShouldBeSameAs(a);
    }

    [Fact]
    public async Task OpenAsync_AfterDispose_ReturnsNewInstance()
    {
        SetupRepo();
        var path = Path.GetTempPath();

        var first = await _factory.OpenAsync(path);
        first.ShouldNotBeNull();
        await first!.DisposeAsync();

        var second = await _factory.OpenAsync(path);

        second.ShouldNotBeNull();
        second.ShouldNotBeSameAs(first);
    }
}
