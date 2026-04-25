using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Moq;
using Shouldly;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;
using Xunit;

namespace TerminalHost.Tests.Services;

public class SessionStateStoreTests
{
    private readonly Mock<IConfigurationService> _config = new();
    private readonly Mock<IGitWorktreeService> _worktree = new();
    private readonly Mock<IGitProcessRunner> _git = new();
    private AppConfiguration _appConfig = new();

    private SessionStateStore Build(TimelineState? initial = null)
    {
        _appConfig = new AppConfiguration { TimelineState = initial };
        _config.Setup(x => x.Load(It.IsAny<string?>())).Returns(() => _appConfig);
        _config.Setup(x => x.Save(It.IsAny<AppConfiguration>(), It.IsAny<string?>()))
            .Callback<AppConfiguration, string?>((cfg, _) => _appConfig = cfg);
        return new SessionStateStore(_config.Object, _worktree.Object, _git.Object);
    }

    [Fact]
    public void Load_PullsTimelineStateFromConfig()
    {
        var existing = new TimelineState { Enabled = true };
        existing.Intents.Add(Intent.Create("Existing", "main", "C:/work"));
        var store = Build(existing);

        store.IsEnabled.ShouldBeTrue();
        store.GetAllIntents().ShouldHaveSingleItem();
    }

    [Fact]
    public void Enable_TogglesAndPersists_AndFiresEventOnce()
    {
        var store = Build();
        var fired = 0;
        store.EnabledChanged += (_, _) => fired++;

        store.Enable();
        store.Enable(); // idempotent

        store.IsEnabled.ShouldBeTrue();
        fired.ShouldBe(1);
        _config.Verify(x => x.Save(It.IsAny<AppConfiguration>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public void Disable_PausesFocus_AndFiresEvent()
    {
        var store = Build(new TimelineState { Enabled = true, FocusStartTime = DateTime.UtcNow.AddMinutes(-5) });
        var focusFired = 0;
        store.FocusStateChanged += (_, _) => focusFired++;

        store.Disable();

        store.IsEnabled.ShouldBeFalse();
        store.IsFocusing.ShouldBeFalse();
        store.GetTotalFocusTime().TotalMinutes.ShouldBeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task CreateIntentFromExistingFolder_AddsIntent_AndFiresChange()
    {
        var store = Build();
        var fired = 0;
        store.IntentsChanged += (_, _) => fired++;

        _git.Setup(x => x.RunGitCommandAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("feature-x\n");

        var intent = await store.CreateIntentFromExistingFolderAsync("Demo", "C:/repo");

        intent.Name.ShouldBe("Demo");
        intent.BranchName.ShouldBe("feature-x");
        store.GetAllIntents().ShouldHaveSingleItem();
        fired.ShouldBe(1);
    }

    [Fact]
    public void UpdateIntentStatus_FiresIntentsChanged_AndCurrentChangedWhenCurrent()
    {
        var i = Intent.Create("A", "main", "C:/work");
        var initial = new TimelineState { CurrentIntentId = i.Id };
        initial.Intents.Add(i);
        initial.IntentOrder.Add(i.Id);
        var store = Build(initial);

        var intentsFired = 0;
        var currentFired = 0;
        store.IntentsChanged += (_, _) => intentsFired++;
        store.CurrentIntentChanged += (_, _) => currentFired++;

        store.UpdateIntentStatus(i.Id, IntentStatus.Completed);

        intentsFired.ShouldBe(1);
        currentFired.ShouldBe(1);
        store.GetIntent(i.Id)!.Status.ShouldBe(IntentStatus.Completed);
        store.GetIntent(i.Id)!.CompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task DeleteIntent_RemovesFromState_AndClearsCurrent()
    {
        var i = Intent.Create("A", "main", "C:/work");
        var initial = new TimelineState { CurrentIntentId = i.Id };
        initial.Intents.Add(i);
        initial.IntentOrder.Add(i.Id);
        var store = Build(initial);

        var currentFired = 0;
        store.CurrentIntentChanged += (_, _) => currentFired++;

        var deleted = await store.DeleteIntentAsync(i.Id, removeWorktree: false);

        deleted.ShouldBeTrue();
        store.GetIntent(i.Id).ShouldBeNull();
        currentFired.ShouldBe(1);
        // No worktree removal requested
        _worktree.Verify(x => x.RemoveWorktreeAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void FindIntentByWorkingDirectory_NormalizesAndMatchesCaseInsensitive()
    {
        var path = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var i = Intent.Create("A", "main", path);
        var initial = new TimelineState();
        initial.Intents.Add(i);
        initial.IntentOrder.Add(i.Id);
        var store = Build(initial);

        var trailing = path + Path.DirectorySeparatorChar;
        var found = store.FindIntentByWorkingDirectory(trailing.ToUpperInvariant());

        found.ShouldNotBeNull();
        found!.Id.ShouldBe(i.Id);
    }

    [Fact]
    public void EnsureIntentForWorkingDirectory_CreatesIntent_WhenMissing()
    {
        var store = Build();
        var fired = 0;
        store.IntentsChanged += (_, _) => fired++;

        var path = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var intent = store.EnsureIntentForWorkingDirectory(path, "MyProject");

        intent.Name.ShouldBe("MyProject");
        store.GetAllIntents().ShouldHaveSingleItem();
        fired.ShouldBe(1);
    }

    [Fact]
    public void EnsureIntentForWorkingDirectory_ReusesExisting_WhenPresent()
    {
        var path = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var existing = Intent.Create("Pre-existing", "main", path);
        var initial = new TimelineState();
        initial.Intents.Add(existing);
        initial.IntentOrder.Add(existing.Id);
        var store = Build(initial);

        var fired = 0;
        store.IntentsChanged += (_, _) => fired++;

        var intent = store.EnsureIntentForWorkingDirectory(path, "Different");

        intent.Id.ShouldBe(existing.Id);
        intent.Name.ShouldBe("Pre-existing");
        store.GetAllIntents().Count.ShouldBe(1);
        fired.ShouldBe(0);
    }

    [Fact]
    public void StartFocusTimer_PauseFocusTimer_FiresFocusStateChanged()
    {
        var store = Build();
        var states = new List<bool>();
        store.FocusStateChanged += (_, b) => states.Add(b);

        store.StartFocusTimer();
        store.PauseFocusTimer();

        store.IsFocusing.ShouldBeFalse();
        states.ShouldBe(new[] { true, false });
    }

    [Fact]
    public void ReorderIntent_PutsIdAtTheRequestedPosition()
    {
        var a = Intent.Create("A", "main", "C:/a");
        var b = Intent.Create("B", "main", "C:/b");
        var c = Intent.Create("C", "main", "C:/c");
        var initial = new TimelineState();
        initial.Intents.AddRange(new[] { a, b, c });
        initial.IntentOrder.AddRange(new[] { a.Id, b.Id, c.Id });
        var store = Build(initial);

        store.ReorderIntent(c.Id, 0);

        store.GetOrderedIntents().Select(i => i.Id).ShouldBe(new[] { c.Id, a.Id, b.Id });
    }
}
