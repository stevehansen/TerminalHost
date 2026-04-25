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

public class TaskAggregatorTests
{
    private readonly Mock<ITaskService> _taskService = new();
    private readonly Mock<IClaudeTaskFileService> _fileService = new();
    private readonly Mock<IClaudeTaskDetectionService> _detectionService = new();

    private TaskAggregator BuildAggregator(
        IReadOnlyList<FocusTask>? manual = null,
        IReadOnlyList<FocusTask>? file = null,
        IReadOnlyList<FocusTask>? detected = null,
        bool includeFile = true,
        bool includeDetection = true)
    {
        _taskService.Setup(x => x.GetAllTasks()).Returns(manual ?? Array.Empty<FocusTask>());
        _fileService.Setup(x => x.GetAllTasks()).Returns(file ?? Array.Empty<FocusTask>());
        _detectionService.Setup(x => x.GetAllClaudeTasks()).Returns(detected ?? Array.Empty<FocusTask>());

        return new TaskAggregator(
            _taskService.Object,
            includeFile ? _fileService.Object : null,
            includeDetection ? _detectionService.Object : null);
    }

    private static FocusTask Task(
        string id,
        string? claudeTaskId = null,
        string? claudeSessionId = null,
        IEnumerable<string>? projectPaths = null,
        FocusTaskStatus status = FocusTaskStatus.NotStarted) =>
        new()
        {
            Id = id,
            Title = id,
            ClaudeTaskId = claudeTaskId,
            ClaudeSessionId = claudeSessionId,
            ProjectPaths = projectPaths?.ToList() ?? new List<string>(),
            Status = status
        };

    [Fact]
    public void GetAll_PassesThroughTasksFromASingleSource()
    {
        var t = Task("t1");
        var agg = BuildAggregator(manual: new[] { t });

        var result = agg.GetAll();

        result.ShouldHaveSingleItem();
        result[0].ShouldBeSameAs(t);
    }

    [Fact]
    public void GetAll_ManualSourceWinsOverFileForSameClaudeTaskIdAndSession()
    {
        var manual = Task("m1", claudeTaskId: "abc", claudeSessionId: "s1");
        var fromFile = Task("f1", claudeTaskId: "abc", claudeSessionId: "s1");
        var agg = BuildAggregator(manual: new[] { manual }, file: new[] { fromFile });

        var result = agg.GetAll();

        result.ShouldHaveSingleItem();
        result[0].ShouldBeSameAs(manual);
    }

    [Fact]
    public void GetAll_FileSourceWinsOverDetectionForSameIdentity()
    {
        var fromFile = Task("f1", claudeTaskId: "abc", claudeSessionId: "s1");
        var detected = Task("d1", claudeTaskId: "abc", claudeSessionId: "s1");
        var agg = BuildAggregator(file: new[] { fromFile }, detected: new[] { detected });

        var result = agg.GetAll();

        result.ShouldHaveSingleItem();
        result[0].ShouldBeSameAs(fromFile);
    }

    [Fact]
    public void GetAll_SameClaudeTaskIdAcrossDifferentSessions_BothAppear()
    {
        var inS1 = Task("f1", claudeTaskId: "abc", claudeSessionId: "session-1");
        var inS2 = Task("f2", claudeTaskId: "abc", claudeSessionId: "session-2");
        var agg = BuildAggregator(file: new[] { inS1, inS2 });

        var result = agg.GetAll();

        result.Count.ShouldBe(2);
        result.ShouldContain(inS1);
        result.ShouldContain(inS2);
    }

    [Fact]
    public void GetAll_FallsBackToClaudeTaskIdWhenSessionMissing()
    {
        var first = Task("a", claudeTaskId: "abc");
        var dupe = Task("b", claudeTaskId: "abc");
        var agg = BuildAggregator(file: new[] { first, dupe });

        var result = agg.GetAll();

        result.ShouldHaveSingleItem();
        result[0].ShouldBeSameAs(first);
    }

    [Fact]
    public void GetAll_FallsBackToTaskIdWhenNoClaudeMetadata()
    {
        var a = Task("manual-1");
        var b = Task("manual-2");
        var agg = BuildAggregator(manual: new[] { a, b });

        var result = agg.GetAll();

        result.Count.ShouldBe(2);
    }

    [Fact]
    public void GetAll_NullOptionalSources_DegradedModeStillWorks()
    {
        var manual = Task("m1");
        var agg = BuildAggregator(
            manual: new[] { manual },
            includeFile: false,
            includeDetection: false);

        var result = agg.GetAll();

        result.ShouldHaveSingleItem();
        result[0].ShouldBeSameAs(manual);
    }

    [Fact]
    public void Changed_FiresWhenTaskServiceRaisesTasksChanged()
    {
        var agg = BuildAggregator();
        var fired = 0;
        agg.Changed += (_, _) => fired++;

        _taskService.Raise(x => x.TasksChanged += null, EventArgs.Empty);

        fired.ShouldBe(1);
    }

    [Fact]
    public void Changed_FiresWhenFileServiceRaisesTasksChanged()
    {
        var agg = BuildAggregator();
        var fired = 0;
        agg.Changed += (_, _) => fired++;

        _fileService.Raise(x => x.TasksChanged += null, EventArgs.Empty);

        fired.ShouldBe(1);
    }

    [Fact]
    public void Changed_FiresWhenDetectionServiceRaisesClaudeTaskChanged()
    {
        var agg = BuildAggregator();
        var fired = 0;
        agg.Changed += (_, _) => fired++;

        _detectionService.Raise(
            x => x.ClaudeTaskChanged += null,
            new ClaudeTaskEventArgs
            {
                Task = Task("x"),
                EventType = ClaudeTaskEventType.Created,
                SessionId = Guid.NewGuid()
            });

        fired.ShouldBe(1);
    }

    [Fact]
    public void Dispose_UnsubscribesFromAllSources()
    {
        var agg = BuildAggregator();
        var fired = 0;
        agg.Changed += (_, _) => fired++;

        agg.Dispose();
        _taskService.Raise(x => x.TasksChanged += null, EventArgs.Empty);
        _fileService.Raise(x => x.TasksChanged += null, EventArgs.Empty);

        fired.ShouldBe(0);
    }

    [Fact]
    public void GetForWorkspace_ManualTaskRequiresExplicitProjectPathMatch()
    {
        var workspace = Path.GetTempPath();
        var matching = Task("m1", projectPaths: new[] { workspace });
        var unscoped = Task("m2");
        var elsewhere = Task("m3", projectPaths: new[] { Path.Combine(Path.GetTempPath(), "other") });
        var agg = BuildAggregator(manual: new[] { matching, unscoped, elsewhere });

        var result = agg.GetForWorkspace(workspace);

        result.ShouldHaveSingleItem();
        result[0].ShouldBeSameAs(matching);
    }

    [Fact]
    public void GetForWorkspace_ClaudeFileTaskWithoutProjectPaths_IsIncluded()
    {
        var workspace = Path.GetTempPath();
        var unscoped = Task("f1", claudeTaskId: "abc", claudeSessionId: "s1");
        var agg = BuildAggregator(file: new[] { unscoped });

        var result = agg.GetForWorkspace(workspace);

        result.ShouldHaveSingleItem();
        result[0].ShouldBeSameAs(unscoped);
    }

    [Fact]
    public void GetForWorkspace_ClaudeDetectionTaskScopedElsewhere_IsExcluded()
    {
        var workspace = Path.GetTempPath();
        var elsewhere = Task("d1", claudeTaskId: "abc", claudeSessionId: "s1",
            projectPaths: new[] { Path.Combine(Path.GetTempPath(), "other") });
        var agg = BuildAggregator(detected: new[] { elsewhere });

        var result = agg.GetForWorkspace(workspace);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void GetForWorkspace_NormalizesPathsCaseInsensitively()
    {
        var workspace = Path.GetTempPath();
        var upperCased = Task("m1", projectPaths: new[] { workspace.ToUpperInvariant() });
        var agg = BuildAggregator(manual: new[] { upperCased });

        var result = agg.GetForWorkspace(workspace.ToLowerInvariant());

        result.ShouldHaveSingleItem();
    }

    [Fact]
    public void GetForWorkspace_EmptyPath_ReturnsEmpty()
    {
        var agg = BuildAggregator(manual: new[] { Task("m1") });

        agg.GetForWorkspace(string.Empty).ShouldBeEmpty();
    }
}
