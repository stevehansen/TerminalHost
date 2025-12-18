using Moq;
using Shouldly;
using TerminalHost.Domain;
using TerminalHost.Services;
using TerminalHost.ViewModels;

namespace TerminalHost.Tests.ViewModels;

public class MainViewModelTests
{
    private readonly Mock<IProfileRegistry> _mockProfileRegistry;
    private readonly Mock<ISessionManager> _mockSessionManager;
    private readonly Mock<ITerminalControlFactory> _mockTerminalFactory;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly Mock<IStatisticsService> _mockStatisticsService;
    private readonly Mock<IGitStatusService> _mockGitStatusService;
    private readonly Mock<ILinkDetectionService> _mockLinkDetectionService;
    private readonly Mock<IProjectDetectionService> _mockProjectDetectionService;
    private readonly Mock<IRunUrlDetectionService> _mockRunUrlDetectionService;
    private readonly Mock<IFilePreviewService> _mockFilePreviewService;
    private readonly Mock<IFileEditService> _mockFileEditService;
    private readonly Mock<IFileExplorerService> _mockFileExplorerService;
    private readonly Mock<DetectedLinksViewModel> _mockDetectedLinksViewModel;
    private readonly Mock<IFileSystem> _mockFileSystem;
    private readonly Mock<IDialogService> _mockDialogService;
    private readonly Mock<IClaudeCommandService> _mockClaudeCommandService;
    private readonly Mock<ITaskService> _mockTaskService;
    private readonly Mock<IAiAssistantService> _mockAiAssistantService;
    private readonly Mock<IGitHubService> _mockGitHubService;
    private readonly Mock<IMarkdownService> _mockMarkdownService;
    private readonly Mock<IProcessService> _mockProcessService;
    private readonly Mock<IToastService> _mockToastService;

    private readonly MainViewModel _mainViewModel;

    public MainViewModelTests()
    {
        _mockProfileRegistry = new Mock<IProfileRegistry>();
        _mockSessionManager = new Mock<ISessionManager>();
        _mockTerminalFactory = new Mock<ITerminalControlFactory>();
        _mockConfigService = new Mock<IConfigurationService>();
        _mockStatisticsService = new Mock<IStatisticsService>();
        _mockGitStatusService = new Mock<IGitStatusService>();
        _mockLinkDetectionService = new Mock<ILinkDetectionService>();
        _mockProjectDetectionService = new Mock<IProjectDetectionService>();
        _mockRunUrlDetectionService = new Mock<IRunUrlDetectionService>();
        _mockFilePreviewService = new Mock<IFilePreviewService>();
        _mockFileEditService = new Mock<IFileEditService>();
        _mockFileExplorerService = new Mock<IFileExplorerService>();
        _mockFileSystem = new Mock<IFileSystem>();
        _mockDialogService = new Mock<IDialogService>();
        _mockClaudeCommandService = new Mock<IClaudeCommandService>();
        _mockTaskService = new Mock<ITaskService>();
        _mockAiAssistantService = new Mock<IAiAssistantService>();
        _mockGitHubService = new Mock<IGitHubService>();
        _mockMarkdownService = new Mock<IMarkdownService>();
        _mockProcessService = new Mock<IProcessService>();
        _mockToastService = new Mock<IToastService>();

        // Setup default behaviors for IAiAssistantService
        var defaultAssistant = new AiAssistant
        {
            Id = "claude",
            Name = "Claude Code",
            Command = "claude.exe",
            Icon = "Claude",
            Enabled = true,
            IsDefault = true
        };
        _mockAiAssistantService.Setup(ai => ai.GetAssistantForDirectory(It.IsAny<string>())).Returns(defaultAssistant);
        _mockAiAssistantService.Setup(ai => ai.GetEnabledAssistants()).Returns(new List<AiAssistant> { defaultAssistant });
        _mockAiAssistantService.Setup(ai => ai.GetDefaultAssistant()).Returns(defaultAssistant);

        // Setup default behaviors for IClaudeCommandService
        _mockClaudeCommandService.Setup(cs => cs.GlobalCommands).Returns(new List<ClaudeCommand>());
        _mockClaudeCommandService.Setup(cs => cs.GetAllCommands(It.IsAny<string?>())).Returns(new List<ClaudeCommand>());

        // Pass constructor arguments for DetectedLinksViewModel mock
        _mockDetectedLinksViewModel = new Mock<DetectedLinksViewModel>(_mockLinkDetectionService.Object, _mockFilePreviewService.Object);

        // Setup default behaviors for mocks
        _mockProfileRegistry.Setup(pr => pr.Settings).Returns(new AppSettings
        {
            CustomCommand = "claude.exe",
            CustomCommandName = "Claude Code",
            CustomCommandIcon = "🤖",
            ShellCommand = "pwsh.exe",
            ShellCommandName = "PowerShell",
            ShellCommandIcon = "💻",
            ConfirmOnClose = true 
        });
        
        // Mock AppConfiguration with a default empty OpenFolders list
        _mockConfigService.Setup(cs => cs.Load()).Returns(new AppConfiguration 
        {
            OpenFolders = new List<string>() ,
            QuickCommands = new List<QuickCommand>()
        });

        // Use deferred execution for creating the control to avoid STA/MTA issues during test class construction
        _mockTerminalFactory.Setup(tf => tf.CreateTerminalControl(It.IsAny<TerminalSession>()))
            .Returns(() => new EasyWindowsTerminalControl.EasyTerminalControl());
        
        _mockGitStatusService.Setup(gs => gs.GetGitStatusAsync(It.IsAny<string>()))
            .ReturnsAsync(new GitStatus { IsGitRepository = false }); 
        
        _mockProjectDetectionService.Setup(pd => pd.GetOrCreateConfigurations(
            It.IsAny<string>(), It.IsAny<DirectorySettings>()))
            .Returns(new List<RunConfiguration> { new RunConfiguration { Id = "shell", Name = "Shell", Command = "", IsDefault = true } });

        // Default mock for DirectoryExists, can be overridden by specific tests
        _mockFileSystem.Setup(fs => fs.DirectoryExists(It.IsAny<string>())).Returns(true);

        _mainViewModel = new MainViewModel(
            _mockProfileRegistry.Object,
            _mockSessionManager.Object,
            _mockTerminalFactory.Object,
            _mockConfigService.Object,
            _mockStatisticsService.Object,
            _mockGitStatusService.Object,
            _mockLinkDetectionService.Object,
            _mockProjectDetectionService.Object,
            _mockRunUrlDetectionService.Object,
            _mockDetectedLinksViewModel.Object,
            _mockFileSystem.Object,
            _mockDialogService.Object,
            _mockFileExplorerService.Object,
            _mockFilePreviewService.Object,
            _mockFileEditService.Object,
            _mockClaudeCommandService.Object,
            _mockTaskService.Object,
            _mockAiAssistantService.Object,
            _mockGitHubService.Object,
            _mockMarkdownService.Object,
            _mockProcessService.Object,
            _mockToastService.Object);
    }

    // Helper to run tests in STA thread
    private static void RunInSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
        {
            throw exception;
        }
    }

    [Fact]
    public void OpenProjectTab_ShouldAddNewTab_WhenDirectoryIsNotAlreadyOpen()
    {
        RunInSta(() =>
        {
            // Arrange
            var workingDirectory = "P:\\NewProject";
            _mockFileSystem.Setup(fs => fs.DirectoryExists(workingDirectory)).Returns(true);

            // Act
            _mainViewModel.OpenProjectTab(workingDirectory);

            // Assert
            _mainViewModel.Tabs.Count.ShouldBe(1);
            var addedTab = _mainViewModel.SelectedTab.ShouldBeOfType<TerminalPairTabViewModel>(); 
            addedTab.Pair.WorkingDirectory.ShouldBe(workingDirectory);

            _mockTerminalFactory.Verify(tf => tf.CreateTerminalControl(It.IsAny<TerminalSession>()), Times.Exactly(2));
            _mockSessionManager.Verify(sm => sm.TrackSession(It.IsAny<TerminalSession>()), Times.Exactly(2));
            _mockGitStatusService.Verify(gs => gs.GetGitStatusAsync(workingDirectory), Times.Once);
            _mockProjectDetectionService.Verify(pd => pd.GetOrCreateConfigurations(
                workingDirectory, It.IsAny<DirectorySettings>()), Times.Once);
            _mockDialogService.Verify(ds => ds.ShowError(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        });
    }

    [Fact]
    public void OpenProjectTab_ShouldFocusExistingTab_WhenDirectoryIsAlreadyOpen()
    {
        RunInSta(() =>
        {
            // Arrange
            var workingDirectory = "P:\\ExistingProject";
            _mockFileSystem.Setup(fs => fs.DirectoryExists(workingDirectory)).Returns(true);

            // First, add a tab for the existing directory
            _mainViewModel.OpenProjectTab(workingDirectory);
            var existingTab = _mainViewModel.SelectedTab;

            // Ensure current selected tab is not the one we want to open again (simulate different tab selected)
            // Fix: Create mock pair with different working directory via constructor to avoid duplicate detection logic during setup
            var mockTerminalPair = new Mock<TerminalPair>(workingDirectory + "_other", new Profile(), new Profile(), _mockStatisticsService.Object);
            var mockTabViewModel = new Mock<TerminalPairTabViewModel>(mockTerminalPair.Object, "", "", _mockStatisticsService.Object);
            // mockTabViewModel.Setup(t => t.Pair.WorkingDirectory).Returns(workingDirectory + "_other"); // REMOVED as unsupported
            
            _mainViewModel.Tabs.Add(mockTabViewModel.Object); // Add another tab

            _mainViewModel.SelectedTab = _mainViewModel.Tabs.Last(); // Select the new tab

            // Act
            _mainViewModel.OpenProjectTab(workingDirectory);

            // Assert
            _mainViewModel.Tabs.Count.ShouldBe(2); // Should not add a new one
            _mainViewModel.SelectedTab.ShouldBe(existingTab); // Should focus the existing tab
            // Verify no new terminals/sessions were created/tracked for the second call
            _mockTerminalFactory.Verify(tf => tf.CreateTerminalControl(It.IsAny<TerminalSession>()), Times.Exactly(2)); 
            _mockSessionManager.Verify(sm => sm.TrackSession(It.IsAny<TerminalSession>()), Times.Exactly(2));
            _mockDialogService.Verify(ds => ds.ShowError(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        });
    }

    [Fact]
    public void OpenProjectTab_ShouldShowError_WhenDirectoryDoesNotExist()
    {
        RunInSta(() =>
        {
            // Arrange
            var nonExistentDirectory = "P:\\NonExistent";
            _mockFileSystem.Setup(fs => fs.DirectoryExists(nonExistentDirectory)).Returns(false);

            // Act
            _mainViewModel.OpenProjectTab(nonExistentDirectory);

            // Assert
            _mainViewModel.Tabs.ShouldBeEmpty();
            _mockDialogService.Verify(ds => ds.ShowError(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        });
    }

    [Fact]
    public void RestoreOpenFolders_ShouldOpenExistingFolders()
    {
        RunInSta(() =>
        {
            // Arrange
            var folder1 = "P:\\Folder1";
            var folder2 = "P:\\Folder2";
            var config = new AppConfiguration
            {
                OpenFolders = new List<string> { folder1, folder2 },
                Settings = new AppSettings() // Need default settings for profiles
            };
            _mockConfigService.Setup(cs => cs.Load()).Returns(config);

            _mockFileSystem.Setup(fs => fs.DirectoryExists(folder1)).Returns(true);
            _mockFileSystem.Setup(fs => fs.DirectoryExists(folder2)).Returns(true);

            // Act
            _mainViewModel.Initialize(); // RestoreOpenFolders is called by Initialize

            // Assert
            _mainViewModel.Tabs.Count.ShouldBe(2);
            _mainViewModel.Tabs.OfType<TerminalPairTabViewModel>().ShouldContain(t => t.Pair.WorkingDirectory == folder1);
            _mainViewModel.Tabs.OfType<TerminalPairTabViewModel>().ShouldContain(t => t.Pair.WorkingDirectory == folder2);
            // Called twice: once in RestoreOpenFolders loop, once inside OpenProjectTab
            _mockFileSystem.Verify(fs => fs.DirectoryExists(folder1), Times.Exactly(2));
            _mockFileSystem.Verify(fs => fs.DirectoryExists(folder2), Times.Exactly(2));
        });
    }

    [Fact]
    public void RestoreOpenFolders_ShouldNotOpenNonExistentFolders()
    {
        RunInSta(() =>
        {
            // Arrange
            var folder1 = "P:\\Folder1";
            var folder2 = "P:\\NonExistentFolder";
            var config = new AppConfiguration
            {
                OpenFolders = new List<string> { folder1, folder2 },
                Settings = new AppSettings()
            };
            _mockConfigService.Setup(cs => cs.Load()).Returns(config);

            _mockFileSystem.Setup(fs => fs.DirectoryExists(folder1)).Returns(true);
            _mockFileSystem.Setup(fs => fs.DirectoryExists(folder2)).Returns(false); // Non-existent

            // Act
            _mainViewModel.Initialize();

            // Assert
            _mainViewModel.Tabs.Count.ShouldBe(1); // Only folder1 should be opened
            _mainViewModel.Tabs.OfType<TerminalPairTabViewModel>().ShouldContain(t => t.Pair.WorkingDirectory == folder1);
            // Folder1 called twice (Restore check + Open check)
            _mockFileSystem.Verify(fs => fs.DirectoryExists(folder1), Times.Exactly(2));
            // Folder2 called once (Restore check) - fails so OpenProjectTab is not called
            _mockFileSystem.Verify(fs => fs.DirectoryExists(folder2), Times.Once);
            _mockDialogService.Verify(ds => ds.ShowError(It.IsAny<string>(), It.IsAny<string>()), Times.Never); // No error for non-existent
        });
    }
}