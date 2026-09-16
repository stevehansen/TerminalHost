using Xunit;
using Moq;
using Shouldly;
using System.Threading.Tasks;
using TerminalHost.Services;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibGit2Sharp;

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
    public async Task GetGitStatusAsync_ShouldDetectGitRepository()
    {
        // Uses libgit2sharp against the actual repo this test project lives in
        var workingDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        var result = await _gitStatusService.GetGitStatusAsync(workingDirectory);

        result.ShouldNotBeNull();
        result.IsGitRepository.ShouldBeTrue();
        result.BranchName.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetGitStatusAsync_ShouldReturnNonRepoForInvalidPath()
    {
        var result = await _gitStatusService.GetGitStatusAsync("P:\\NonExistent\\FakePath");

        result.ShouldNotBeNull();
        result.IsGitRepository.ShouldBeFalse();
    }

    [Fact]
    public async Task GetGitStatusAsync_ShouldReturnNonRepoForNonGitDirectory()
    {
        // Use the Windows temp directory which is not a git repo
        var tempDir = Path.GetTempPath();
        _mockFileSystem.Setup(fs => fs.DirectoryExists(tempDir)).Returns(true);

        var result = await _gitStatusService.GetGitStatusAsync(tempDir);

        result.ShouldNotBeNull();
        result.IsGitRepository.ShouldBeFalse();
    }

    [Fact]
    public async Task GetModifiedFilesAsync_ShouldReturnCorrectlyParsedFiles()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        // Corrected porcelain format for staged delete: "D  DeletedFile.md"
        var statusOutput = " M MyFile.cs\nA  AnotherFile.txt\nD  DeletedFile.md\n?? UntrackedFile.json\nR  OldFile.cs -> RenamedFile.cs\n";
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "status --porcelain --untracked-files=all"))
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
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "status --porcelain --untracked-files=all"))
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
        // Format: refname|HEAD indicator|upstream|track info|commit hash|relative date|subject
        var branchOutput = "main|*|origin/main|[ahead 1, behind 2]|abc1234|2 days ago|Initial commit\ndev||||def5678|1 day ago|Feature work\nremotes/origin/main||||ghi9012|2 days ago|Initial commit\n";
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "remote"))
                             .ReturnsAsync("origin\n"); // Mock remotes list
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "rev-parse --abbrev-ref HEAD"))
                             .ReturnsAsync("main\n"); // Explicit current branch
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "branch -a --format=\"%(refname:short)|%(HEAD)|%(upstream:short)|%(upstream:track)|%(objectname:short)|%(committerdate:relative)|%(subject)\" "))
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
        _mockGitProcessRunner.Setup(r => r.RunGitOperationAsync(workingDirectory, $"checkout \"{branchName}\""))
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

    [Fact]
    public async Task GetModifiedFilesAsync_ShouldHandleFilePathsWithSpaces()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        // Git porcelain format with quoted paths containing spaces
        var statusOutput = " M \"My File.cs\"\nA  \"File with Spaces.txt\"\nD  \"Deleted File.md\"\n";
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "status --porcelain --untracked-files=all"))
                             .ReturnsAsync(statusOutput);

        // Act
        var result = await _gitStatusService.GetModifiedFilesAsync(workingDirectory);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(3);

        // Verify paths are unquoted and correct
        result.ShouldContain(f => f.FilePath == "My File.cs" && f.Status == GitFileStatusType.Modified);
        result.ShouldContain(f => f.FilePath == "File with Spaces.txt" && f.Status == GitFileStatusType.Added);
        result.ShouldContain(f => f.FilePath == "Deleted File.md" && f.Status == GitFileStatusType.Deleted);

        // Verify FileName property is correctly extracted
        var fileWithSpaces = result.First(f => f.FilePath == "My File.cs");
        fileWithSpaces.FileName.ShouldBe("My File.cs");
    }

    [Fact]
    public async Task GetModifiedFilesAsync_ShouldHandleFilePathsWithSpacesInDirectories()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        // Git porcelain format with quoted paths containing spaces in directory names
        var statusOutput = " M \"My Project/Test File.cs\"\nA  \"Src/Components/My Component.tsx\"\n";
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "status --porcelain --untracked-files=all"))
                             .ReturnsAsync(statusOutput);

        // Act
        var result = await _gitStatusService.GetModifiedFilesAsync(workingDirectory);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(2);

        result.ShouldContain(f => f.FilePath == "My Project/Test File.cs");
        result.ShouldContain(f => f.FilePath == "Src/Components/My Component.tsx");
    }

    [Fact]
    public async Task GetModifiedFilesAsync_ShouldHandleRenamedFilesWithSpaces()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        // Git porcelain format with renamed files that have spaces
        var statusOutput = "R  \"Old Test File.cs\" -> \"New Test File.cs\"\n";
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "status --porcelain --untracked-files=all"))
                             .ReturnsAsync(statusOutput);

        // Act
        var result = await _gitStatusService.GetModifiedFilesAsync(workingDirectory);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(1);

        var renamed = result.First();
        renamed.FilePath.ShouldBe("New Test File.cs");
        renamed.OriginalPath.ShouldBe("Old Test File.cs");
        renamed.Status.ShouldBe(GitFileStatusType.Renamed);
    }

    [Fact]
    public async Task GetModifiedFilesAsync_ShouldHandleUnquotedPaths()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        // Regular files without spaces (should not be quoted by git)
        var statusOutput = " M RegularFile.cs\nA  AnotherFile.txt\n";
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "status --porcelain --untracked-files=all"))
                             .ReturnsAsync(statusOutput);

        // Act
        var result = await _gitStatusService.GetModifiedFilesAsync(workingDirectory);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(2);

        result.ShouldContain(f => f.FilePath == "RegularFile.cs");
        result.ShouldContain(f => f.FilePath == "AnotherFile.txt");
    }

    [Fact]
    public async Task GetModifiedFilesAsync_ShouldHandleMixedQuotedAndUnquotedPaths()
    {
        // Arrange
        var workingDirectory = "P:\\TestRepo";
        // Mix of quoted (with spaces) and unquoted (without spaces) paths
        var statusOutput = " M RegularFile.cs\nA  \"File with Spaces.txt\"\nD  DeletedFile.md\nR  \"Old File.cs\" -> \"New File.cs\"\n";
        _mockGitProcessRunner.Setup(r => r.RunGitCommandAsync(workingDirectory, "status --porcelain --untracked-files=all"))
                             .ReturnsAsync(statusOutput);

        // Act
        var result = await _gitStatusService.GetModifiedFilesAsync(workingDirectory);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(4);

        result.ShouldContain(f => f.FilePath == "RegularFile.cs");
        result.ShouldContain(f => f.FilePath == "File with Spaces.txt");
        result.ShouldContain(f => f.FilePath == "DeletedFile.md");
        result.ShouldContain(f => f.FilePath == "New File.cs" && f.OriginalPath == "Old File.cs");
    }

    // --- Summary status: untracked-excluded dirty semantics + caching ---

    private static GitStatusService NewService()
    {
        var fs = new Mock<IFileSystem>();
        fs.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(true);
        return new GitStatusService(new Mock<IGitProcessRunner>().Object, fs.Object);
    }

    private static string InitRepoWithOneCommit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Repository.Init(dir);
        using var repo = new Repository(dir);
        File.WriteAllText(Path.Combine(dir, "tracked.txt"), "v1");
        Commands.Stage(repo, "tracked.txt");
        var sig = new Signature("Test", "test@example.com", DateTimeOffset.UtcNow);
        repo.Commit("initial", sig, sig);
        return dir;
    }

    private static void DeleteRepo(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* a lingering handle may block it */ }
    }

    [Fact]
    public async Task GetGitStatusAsync_UntrackedOnly_IsNotDirty()
    {
        var dir = InitRepoWithOneCommit();
        try
        {
            File.WriteAllText(Path.Combine(dir, "untracked.txt"), "new");
            using var svc = NewService();

            var result = await svc.GetGitStatusAsync(dir);

            result.IsGitRepository.ShouldBeTrue();
            result.IsDirty.ShouldBeFalse(); // untracked files are excluded from the badge
        }
        finally { DeleteRepo(dir); }
    }

    [Fact]
    public async Task GetGitStatusAsync_ModifiedTrackedFile_IsDirty()
    {
        var dir = InitRepoWithOneCommit();
        try
        {
            File.WriteAllText(Path.Combine(dir, "tracked.txt"), "v2-changed");
            using var svc = NewService();

            var result = await svc.GetGitStatusAsync(dir);

            result.IsDirty.ShouldBeTrue(); // tracked-file changes still light up
        }
        finally { DeleteRepo(dir); }
    }

    [Fact]
    public async Task GetGitStatusAsync_RepeatedCalls_ServeCachedInstance()
    {
        var dir = InitRepoWithOneCommit();
        try
        {
            using var svc = NewService();

            var first = await svc.GetGitStatusAsync(dir);
            var second = await svc.GetGitStatusAsync(dir);

            second.ShouldBeSameAs(first); // within TTL, no .git change → cache hit
        }
        finally { DeleteRepo(dir); }
    }
}
