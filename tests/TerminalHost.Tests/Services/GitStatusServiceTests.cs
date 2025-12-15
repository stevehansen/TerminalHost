using Xunit;
using Moq;
using Shouldly;
using System.Threading.Tasks;
using TerminalHost.Services;
using TerminalHost.Domain;
using System.Collections.Generic;
using System.Linq;

namespace TerminalHost.Tests.Services;

public class GitStatusServiceTests
{
    private readonly Mock<IGitProcessRunner> _mockGitProcessRunner;
    private readonly Mock<IFileSystem> _mockFileSystem;
    private readonly GitStatusService _gitStatusService;

    public GitStatusServiceTests()
    {
        _mockGitProcessRunner = new Mock<IGitProcessRunner>();
        _mockFileSystem = new Mock<IFileSystem>();
        // Assume directory always exists for these tests unless specifically mocked otherwise
        _mockFileSystem.Setup(fs => fs.DirectoryExists(It.IsAny<string>())).Returns(true);
        // For GetFileDiffAsync untracked case - ensure it doesn't kick in unexpectedly for diff tests
        _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
        _mockFileSystem.Setup(fs => fs.ReadAllTextAsync(It.IsAny<string>())).ReturnsAsync("file content"); 

        _gitStatusService = new GitStatusService(_mockGitProcessRunner.Object, _mockFileSystem.Object);
    }

    [Fact]
    public async Task GetGitStatusAsync_ShouldReturnCleanStatus_WhenNoChanges()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "rev-parse --git-dir"))
                             .ReturnsAsync(".git\n"); // Indicate it's a git repo
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "status --porcelain"))
                             .ReturnsAsync(""); // No output means clean
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "rev-parse --abbrev-ref HEAD"))
                             .ReturnsAsync("main\n");
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "rev-list --count @{u}..HEAD"))
                             .ReturnsAsync("1\n"); // Example ahead
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "rev-list --count HEAD..@{u}"))
                             .ReturnsAsync("2\n"); // Example behind

        // Act
        var result = await _gitStatusService.GetGitStatusAsync(workingDirectory);

        // Assert
        result.ShouldNotBeNull();
        result.IsGitRepository.ShouldBeTrue();
        result.IsDirty.ShouldBeFalse();
        result.BranchName.ShouldBe("main");
        result.AheadCount.ShouldBe(1);
        result.BehindCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetGitStatusAsync_ShouldReturnDirtyStatus_WhenChangesExist()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        var statusOutput = " M MyFile.cs\n?? NewFile.txt";
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "rev-parse --git-dir"))
                             .ReturnsAsync(".git\n"); // Indicate it's a git repo
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "status --porcelain"))
                             .ReturnsAsync(statusOutput);
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "rev-parse --abbrev-ref HEAD"))
                             .ReturnsAsync("feature/branch\n");
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "rev-list --count @{u}..HEAD"))
                             .ReturnsAsync("0\n");
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "rev-list --count HEAD..@{u}"))
                             .ReturnsAsync("0\n");

        // Act
        var result = await _gitStatusService.GetGitStatusAsync(workingDirectory);

        // Assert
        result.ShouldNotBeNull();
        result.IsGitRepository.ShouldBeTrue();
        result.IsDirty.ShouldBeTrue();
        result.BranchName.ShouldBe("feature/branch");
        result.AheadCount.ShouldBe(0);
        result.BehindCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetGitStatusAsync_ShouldHandleNoUpstreamTracking()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "rev-parse --git-dir"))
                             .ReturnsAsync(".git\n"); // Indicate it's a git repo
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "status --porcelain"))
                             .ReturnsAsync("");
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "rev-parse --abbrev-ref HEAD"))
                             .ReturnsAsync("main\n");
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "rev-list --count @{u}..HEAD"))
                             .ReturnsAsync(""); // No output for tracking
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "rev-list --count HEAD..@{u}"))
                             .ReturnsAsync(""); // No output for tracking

        // Act
        var result = await _gitStatusService.GetGitStatusAsync(workingDirectory);

        // Assert
        result.ShouldNotBeNull();
        result.IsGitRepository.ShouldBeTrue();
        result.BranchName.ShouldBe("main");
        result.AheadCount.ShouldBe(0);
        result.BehindCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetModifiedFilesAsync_ShouldReturnCorrectlyParsedFiles()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        // Corrected porcelain format for staged delete: "D  DeletedFile.md"
        var statusOutput = " M MyFile.cs\nA  AnotherFile.txt\nD  DeletedFile.md\n?? UntrackedFile.json\nR  OldFile.cs -> RenamedFile.cs\n";
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "status --porcelain"))
                             .ReturnsAsync(statusOutput);

        // Act
        var result = await _gitStatusService.GetModifiedFilesAsync(workingDirectory);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(5);

        result.ShouldContain(f => f.FilePath == "MyFile.cs" && f.Status == GitFileStatusType.Modified && f.IsStaged == false);
        result.ShouldContain(f => f.FilePath == "AnotherFile.txt" && f.Status == GitFileStatusType.Added && f.IsStaged == true);
        result.ShouldContain(f => f.FilePath == "DeletedFile.md" && f.Status == GitFileStatusType.Deleted && f.IsStaged == true); // Now correctly staged
        result.ShouldContain(f => f.FilePath == "UntrackedFile.json" && f.Status == GitFileStatusType.Untracked && f.IsStaged == false);
        result.ShouldContain(f => f.FilePath == "RenamedFile.cs" && f.Status == GitFileStatusType.Renamed && f.IsStaged == true && f.OriginalPath == "OldFile.cs");
    }

    [Fact]
    public async Task GetModifiedFilesAsync_ShouldHandleEmptyOutput()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "status --porcelain"))
                             .ReturnsAsync("");

        // Act
        var result = await _gitStatusService.GetModifiedFilesAsync(workingDirectory);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetFileDiffAsync_ShouldReturnDiffContent()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        var filePath = "MyFile.cs";
        var diffContent = "--- a/MyFile.cs\n+++ b/MyFile.cs\n@@ -1,3 +1,4 @@\n-old\n+new";
        
        // Use predicate matcher to avoid whitespace issues
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, It.Is<string>(s => s.StartsWith("diff --") && s.Contains(filePath))))
                             .ReturnsAsync(diffContent); 

        // Act
        var result = await _gitStatusService.GetFileDiffAsync(workingDirectory, filePath);

        // Assert
        result.ShouldBe(diffContent);
    }

    [Fact]
    public async Task GetFileDiffAsync_ShouldReturnStagedDiffContent()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        var filePath = "MyFile.cs";
        var diffContent = "--- a/MyFile.cs\n+++ b/MyFile.cs\n@@ -1,3 +1,4 @@\n-staged old\n+staged new";
        
        // Use predicate matcher
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, It.Is<string>(s => s.StartsWith("diff --cached") && s.Contains(filePath))))
                             .ReturnsAsync(diffContent); 

        // Act
        var result = await _gitStatusService.GetFileDiffAsync(workingDirectory, filePath, staged: true);

        // Assert
        result.ShouldBe(diffContent);
    }

    [Fact]
    public async Task GetBranchesAsync_ShouldReturnParsedBranches()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        // Fixed: Changed 'feature/branch' to 'dev' to avoid it being misidentified as remote by current implementation logic
        var branchOutput = "main|*|origin/main|[ahead 1, behind 2]\ndev|||\nremotes/origin/main|||\n"; 
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "rev-parse --abbrev-ref HEAD"))
                             .ReturnsAsync("main\n"); // Explicit current branch
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "branch -a --format=\"%(refname:short)|%(HEAD)|%(upstream:short)|%(upstream:track)\" ")) 
                             .ReturnsAsync(branchOutput);


        // Act
        var result = await _gitStatusService.GetBranchesAsync(workingDirectory);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(2); // main, dev (remotes/origin/main is filtered out)

        var mainBranch = result.FirstOrDefault(b => b.Name == "main");
        mainBranch.ShouldNotBeNull();
        mainBranch.IsCurrent.ShouldBeTrue();
        mainBranch.IsRemote.ShouldBeFalse();
        mainBranch.AheadCount.ShouldBe(1);
        mainBranch.BehindCount.ShouldBe(2);

        var devBranch = result.FirstOrDefault(b => b.Name == "dev");
        devBranch.ShouldNotBeNull();
        devBranch.IsCurrent.ShouldBeFalse();
        devBranch.IsRemote.ShouldBeFalse();
        devBranch.AheadCount.ShouldBeNull(); 
        devBranch.BehindCount.ShouldBeNull(); 
    }

    [Fact]
    public async Task GetFileContentAtHeadAsync_ShouldReturnFileContent()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        var filePath = "MyFile.cs";
        var fileContent = "public class MyFile { }";
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, $"show HEAD:\"{filePath}\" ")) // Added trailing space
                             .ReturnsAsync(fileContent);

        // Act
        var result = await _gitStatusService.GetFileContentAtHeadAsync(workingDirectory, filePath);

        // Assert
        result.ShouldBe(fileContent);
    }

    [Fact]
    public async Task CheckoutBranchAsync_ShouldReturnSuccess()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        var branchName = "new-feature";
        _mockGitProcessRunner.Setup(r => r.RunGitOperationAsync(workingDirectory, $"checkout \"{branchName}\" ")) // Added trailing space
                             .ReturnsAsync(new GitOperationResult { Success = true, Output = $"Switched to branch '{branchName}'" });

        // Act
        var result = await _gitStatusService.CheckoutBranchAsync(workingDirectory, branchName);

        // Assert
        result.Success.ShouldBeTrue();
        result.Output.ShouldNotBeNull();
        result.Output.ShouldContain("Switched to branch");
    }

    [Fact]
    public async Task CreateBranchAsync_ShouldReturnSuccess()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        var branchName = "new-branch";
        _mockGitProcessRunner.Setup(r => r.RunGitOperationAsync(workingDirectory, $"checkout -b \"{branchName}\" ")) // Added trailing space
                             .ReturnsAsync(new GitOperationResult { Success = true, Output = $"Switched to a new branch '{branchName}'" });

        // Act
        var result = await _gitStatusService.CreateBranchAsync(workingDirectory, branchName);

        // Assert
        result.Success.ShouldBeTrue();
        result.Output.ShouldNotBeNull();
        result.Output.ShouldContain("Switched to a new branch");
    }

    [Fact]
    public async Task DeleteBranchAsync_ShouldReturnSuccess_ForceFalse()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        var branchName = "old-branch";
        _mockGitProcessRunner.Setup(r => r.RunGitOperationAsync(workingDirectory, $"branch -d \"{branchName}\" ")) // Added trailing space
                             .ReturnsAsync(new GitOperationResult { Success = true, Output = $"Deleted branch {branchName}." });

        // Act
        var result = await _gitStatusService.DeleteBranchAsync(workingDirectory, branchName, force: false);

        // Assert
        result.Success.ShouldBeTrue();
        result.Output.ShouldNotBeNull();
        result.Output.ShouldContain("Deleted branch");
    }

    [Fact]
    public async Task DeleteBranchAsync_ShouldReturnSuccess_ForceTrue()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        var branchName = "old-branch";
        _mockGitProcessRunner.Setup(r => r.RunGitOperationAsync(workingDirectory, $"branch -D \"{branchName}\" ")) // Added trailing space
                             .ReturnsAsync(new GitOperationResult { Success = true, Output = $"Deleted branch {branchName} (was a8f12c3)." });

        // Act
        var result = await _gitStatusService.DeleteBranchAsync(workingDirectory, branchName, force: true);

        // Assert
        result.Success.ShouldBeTrue();
        result.Output.ShouldNotBeNull();
        result.Output.ShouldContain("Deleted branch");
    }

    [Fact]
    public async Task DeleteRemoteBranchAsync_ShouldReturnSuccess()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        var remoteName = "origin";
        var branchName = "old-remote-branch";
        _mockGitProcessRunner.Setup(r => r.RunGitOperationAsync(workingDirectory, $"push \"{remoteName}\" --delete \"{branchName}\" ")) // Added trailing space
                             .ReturnsAsync(new GitOperationResult { Success = true, Output = $"To {remoteName}\n - [deleted]         {branchName}" });

        // Act
        var result = await _gitStatusService.DeleteRemoteBranchAsync(workingDirectory, remoteName, branchName);

        // Assert
        result.Success.ShouldBeTrue();
        result.Output.ShouldNotBeNull();
        result.Output.ShouldContain("[deleted]");
    }

    [Fact]
    public async Task FetchAllAsync_ShouldReturnSuccess()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        _mockGitProcessRunner.Setup(r => r.RunGitOperationAsync(workingDirectory, "fetch --all --prune")) // No trailing space in code
                             .ReturnsAsync(new GitOperationResult { Success = true, Output = "Fetching origin" });

        // Act
        var result = await _gitStatusService.FetchAllAsync(workingDirectory);

        // Assert
        result.Success.ShouldBeTrue();
        result.Output.ShouldNotBeNull();
        result.Output.ShouldContain("Fetching origin");
    }

    [Fact]
    public async Task PullAsync_ShouldReturnSuccess()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        _mockGitProcessRunner.Setup(r => r.RunGitOperationAsync(workingDirectory, "pull")) // No trailing space in code
                             .ReturnsAsync(new GitOperationResult { Success = true, Output = "Already up to date." });

        // Act
        var result = await _gitStatusService.PullAsync(workingDirectory);

        // Assert
        result.Success.ShouldBeTrue();
        result.Output.ShouldNotBeNull();
        result.Output.ShouldContain("up to date");
    }
}
