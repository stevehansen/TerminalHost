using Shouldly;
using Moq;
using TerminalHost.Services;
using TerminalHost.ViewModels;

namespace TerminalHost.Tests.ViewModels;

public class SettingsTabViewModelTests
{
    private readonly Mock<IConfigurationService> _configServiceMock;
    private readonly Mock<IDialogService> _dialogServiceMock; // Added mock
    private readonly SettingsTabViewModel _viewModel;

    public SettingsTabViewModelTests()
    {
        _configServiceMock = new Mock<IConfigurationService>();
        _dialogServiceMock = new Mock<IDialogService>(); // Initialize mock
        _viewModel = new SettingsTabViewModel(_configServiceMock.Object, _dialogServiceMock.Object); // Pass mock
    }

    [Fact]
    public void LoadSettings_ShouldLoadJsonFromService()
    {
        // Arrange
        var expectedJson = "{\"test\": true}";
        _configServiceMock.Setup(x => x.LoadRawJson()).Returns(expectedJson);

        // Act
        _viewModel.LoadSettings();

        // Assert
        _viewModel.JsonText.ShouldBe(expectedJson);
        _viewModel.IsDirty.ShouldBeFalse();
        _viewModel.HasError.ShouldBeFalse();
    }

    [Fact]
    public void OnTextChanged_ShouldSetIsDirty_WhenTextChanges()
    {
        // Arrange
        var originalJson = "{\"test\": true}";
        _configServiceMock.Setup(x => x.LoadRawJson()).Returns(originalJson);
        _viewModel.LoadSettings();

        // Act
        _viewModel.OnTextChanged("{\"test\": false}");

        // Assert
        _viewModel.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void Save_ShouldCallSaveRawJson()
    {
        // Arrange
        var originalJson = "{\"test\": true}";
        var newJson = "{\"test\": false}";
        _configServiceMock.Setup(x => x.LoadRawJson()).Returns(originalJson);
        _configServiceMock.Setup(x => x.SaveRawJson(newJson)).Returns((true, null, null));
        _viewModel.LoadSettings();
        _viewModel.JsonText = newJson;

        // Act
        _viewModel.SaveCommand.Execute(null);

        // Assert
        _configServiceMock.Verify(x => x.SaveRawJson(newJson), Times.Once);
        _viewModel.IsDirty.ShouldBeFalse();
    }
}