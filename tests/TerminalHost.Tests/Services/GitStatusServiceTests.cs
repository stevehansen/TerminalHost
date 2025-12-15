using Moq;
using Shouldly;
using TerminalHost.Domain;
using TerminalHost.Services;
using Xunit;

namespace TerminalHost.Tests.Services;

public class GitStatusServiceTests
{
    private readonly Mock<IGitProcessRunner> _gitRunnerMock;
    private readonly Mock<IFileSystem> _fileSystemMock;
    private readonly GitStatusService _service;
    private const string TestPath = "P:\\TestProject";

    public GitStatusServiceTests()
    {
        _gitRunnerMock = new Mock<IGitProcessRunner>();
        _fileSystemMock = new Mock<IFileSystem>();
        _service = new GitStatusService(_gitRunnerMock.Object, _fileSystemMock.Object);
    }

    [Fact]
    public async Task GetGitStatusAsync_ShouldReturnEmpty_WhenDirectoryDoesNotExist()
    {
        // Arrange
        _fileSystemMock.Setup(x => x.DirectoryExists(TestPath)).Returns(false);

        // Act
        var result = await _service.GetGitStatusAsync(TestPath);

        // Assert
        result.IsGitRepository.ShouldBeFalse();
        result.BranchName.ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task GetGitStatusAsync_ShouldReturnEmpty_WhenNotGitRepo()
    {
        // Arrange
        _fileSystemMock.Setup(x => x.DirectoryExists(TestPath)).Returns(true);
        _gitRunnerMock.Setup(x => x.RunGitCommandAsync(TestPath, "rev-parse --git-dir"))
            .ReturnsAsync((string?)null); // git command failed or returned null

        // Act
        var result = await _service.GetGitStatusAsync(TestPath);

        // Assert
        result.IsGitRepository.ShouldBeFalse();
        result.BranchName.ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task GetGitStatusAsync_ShouldReturnStatus_WhenValidRepo()
    {
        // Arrange
        _fileSystemMock.Setup(x => x.DirectoryExists(TestPath)).Returns(true);
        
        // Mock git commands
        _gitRunnerMock.Setup(x => x.RunGitCommandAsync(TestPath, "rev-parse --git-dir"))
            .ReturnsAsync(".git");
            
        _gitRunnerMock.Setup(x => x.RunGitCommandAsync(TestPath, "rev-parse --abbrev-ref HEAD"))
            .ReturnsAsync("main");
            
        _gitRunnerMock.Setup(x => x.RunGitCommandAsync(TestPath, "status --porcelain"))
            .ReturnsAsync("M file.txt"); // Dirty

        _gitRunnerMock.Setup(x => x.RunGitCommandAsync(TestPath, "rev-list --count @{u}..HEAD"))
            .ReturnsAsync("2"); // Ahead by 2

        _gitRunnerMock.Setup(x => x.RunGitCommandAsync(TestPath, "rev-list --count HEAD..@{u}"))
            .ReturnsAsync("1"); // Behind by 1

        // Act
        var result = await _service.GetGitStatusAsync(TestPath);

        // Assert
        result.IsGitRepository.ShouldBeTrue();
        result.BranchName.ShouldBe("main");
        result.IsDirty.ShouldBeTrue();
        result.AheadCount.ShouldBe(2);
        result.BehindCount.ShouldBe(1);
    }
}
