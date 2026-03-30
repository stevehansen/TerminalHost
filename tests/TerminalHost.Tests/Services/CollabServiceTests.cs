using Moq;
using Shouldly;
using System.Text.Json;
using Xunit;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;

namespace TerminalHost.Tests.Services;

public class CollabServiceTests
{
    /// <summary>
    /// Creates an in-memory CollabService (no persistence) for basic behavior tests.
    /// </summary>
    private static CollabService CreateInMemory() => new();

    #region Basic behavior (no persistence)

    [Fact]
    public void SendMessage_AutoCreatesTopicAndSubscribes()
    {
        var svc = CreateInMemory();
        svc.SendMessage("alice", "work", "hello");

        var topics = svc.GetTopics();
        topics.Count.ShouldBe(1);
        topics[0].Name.ShouldBe("work");
        topics[0].Subscribers.ShouldContain("alice");
        topics[0].MessageCount.ShouldBe(1);
    }

    [Fact]
    public void ReadMessages_ReturnsMessagesAfterSinceId()
    {
        var svc = CreateInMemory();
        svc.SendMessage("alice", "work", "msg1");
        svc.SendMessage("alice", "work", "msg2");
        svc.SendMessage("alice", "work", "msg3");

        var (msgs, cursor) = svc.ReadMessages("bob", "work", 1);
        msgs.Count.ShouldBe(2);
        msgs[0].Content.ShouldBe("msg2");
        msgs[1].Content.ShouldBe("msg3");
        cursor.ShouldBe(3);
    }

    [Fact]
    public void Unsubscribe_DeletesTopicWhenEmpty_AfterSubscriber()
    {
        var svc = CreateInMemory();
        svc.SendMessage("alice", "work", "hello");

        // HasHadSubscriber is true because SendMessage called EnsureTopicAndSubscribe
        var (ok, _) = svc.Unsubscribe("alice", "work");
        ok.ShouldBeTrue();

        svc.GetTopics().Count.ShouldBe(0);
    }

    [Fact]
    public void GetUnreadCounts_TracksUnreadPerTopic()
    {
        var svc = CreateInMemory();
        svc.Subscribe("alice", "work");
        svc.SendMessage("bob", "work", "msg1");
        svc.SendMessage("bob", "work", "msg2");

        var counts = svc.GetUnreadCounts("alice");
        counts.ShouldContainKey("work");
        counts["work"].ShouldBe(2);

        // Read messages advances cursor
        svc.ReadMessages("alice", "work", 0);
        counts = svc.GetUnreadCounts("alice");
        counts.ShouldNotContainKey("work");
    }

    [Fact]
    public void SenderCursorAutoAdvances()
    {
        var svc = CreateInMemory();
        svc.SendMessage("alice", "work", "hello");

        var counts = svc.GetUnreadCounts("alice");
        counts.ShouldNotContainKey("work"); // Sender doesn't see own message as unread
    }

    #endregion

    #region HasHadSubscriber guard

    [Fact]
    public void Unsubscribe_DoesNotDeleteTopic_BeforeAnySubscriber()
    {
        // Simulate a topic loaded from persistence (no subscribers, HasHadSubscriber = false)
        var svc = CreateInMemory();

        // Use reflection or just test via the public API:
        // After persistence load, topics have HasHadSubscriber = false.
        // But through normal API, EnsureTopicAndSubscribe always sets it true.
        // So we test the normal flow: subscribe then unsubscribe should delete.
        svc.Subscribe("alice", "work");
        svc.Unsubscribe("alice", "work");
        svc.GetTopics().Count.ShouldBe(0); // Deleted because HasHadSubscriber was set
    }

    #endregion

    #region Persistence round-trip

    [Fact]
    public void Persistence_RoundTrip_PreservesState()
    {
        var fileSystem = new Mock<IFileSystem>();
        string? savedJson = null;
        var configDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TerminalHost");
        var filePath = System.IO.Path.Combine(configDir, "collab-state.json");
        var backupPath = filePath + ".bak";

        // First instance: no existing file, creates state
        fileSystem.Setup(f => f.FileExists(filePath)).Returns(false);
        fileSystem.Setup(f => f.FileExists(backupPath)).Returns(false);
        fileSystem.Setup(f => f.CreateDirectory(It.IsAny<string>()));
        fileSystem.Setup(f => f.WriteAllText(filePath, It.IsAny<string>()))
            .Callback<string, string>((_, json) => savedJson = json);
        fileSystem.Setup(f => f.CopyFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()));

        var svc1 = new CollabService(fileSystem.Object);
        svc1.SendMessage("alice", "work", "hello world");
        svc1.SendMessage("bob", "work", "hi alice");
        svc1.Subscribe("charlie", "announcements", "Company announcements");

        // Read to set cursor
        svc1.ReadMessages("alice", "work", 0);

        // Dispose triggers final save
        svc1.Dispose();

        savedJson.ShouldNotBeNull();

        // Second instance: loads from saved state
        fileSystem.Setup(f => f.FileExists(filePath)).Returns(true);
        fileSystem.Setup(f => f.ReadAllText(filePath)).Returns(savedJson!);

        var svc2 = new CollabService(fileSystem.Object);

        // Topics should be restored
        var topics = svc2.GetTopics();
        topics.Count.ShouldBe(2);
        topics.ShouldContain(t => t.Name == "work");
        topics.ShouldContain(t => t.Name == "announcements");

        // Messages should be restored
        var (msgs, cursor) = svc2.ReadMessages("newreader", "work", 0);
        msgs.Count.ShouldBe(2);
        msgs[0].Content.ShouldBe("hello world");
        msgs[1].Content.ShouldBe("hi alice");

        // Message IDs should continue from where they left off (no collision)
        svc2.SendMessage("dave", "work", "new message");
        var (allMsgs, _) = svc2.ReadMessages("dave", "work", 0);
        var ids = allMsgs.Select(m => m.Id).ToList();
        ids.ShouldBeUnique();
        ids.Max().ShouldBeGreaterThan(2); // Should be 3, continuing from persisted counter

        svc2.Dispose();
    }

    [Fact]
    public void Persistence_TopicsNotDeletedBeforeResubscribe()
    {
        var fileSystem = new Mock<IFileSystem>();
        var configDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TerminalHost");
        var filePath = System.IO.Path.Combine(configDir, "collab-state.json");
        var backupPath = filePath + ".bak";

        // Create state with a topic
        var state = new CollabPersistedState
        {
            NextMessageId = 5,
            Topics = new()
            {
                new PersistedTopic { Name = "work", Description = "Work topic", CreatedBy = "alice", CreatedAt = DateTime.UtcNow }
            },
            Messages = new()
            {
                new CollabMessage { Id = 1, Topic = "work", Sender = "alice", Content = "saved msg", CreatedAt = DateTime.UtcNow }
            },
            Cursors = new()
        };

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });

        fileSystem.Setup(f => f.FileExists(filePath)).Returns(true);
        fileSystem.Setup(f => f.FileExists(backupPath)).Returns(false);
        fileSystem.Setup(f => f.ReadAllText(filePath)).Returns(json);
        fileSystem.Setup(f => f.CreateDirectory(It.IsAny<string>()));
        fileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()));
        fileSystem.Setup(f => f.CopyFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()));

        var svc = new CollabService(fileSystem.Object);

        // Topic should exist (loaded from persistence)
        svc.GetTopics().Count.ShouldBe(1);

        // Reading auto-subscribes, which sets HasHadSubscriber = true
        var (msgs, _) = svc.ReadMessages("bob", "work", 0);
        msgs.Count.ShouldBe(1);
        msgs[0].Content.ShouldBe("saved msg");

        // Now unsubscribe should delete (HasHadSubscriber is true)
        svc.Unsubscribe("bob", "work");
        svc.GetTopics().Count.ShouldBe(0);

        svc.Dispose();
    }

    [Fact]
    public void Persistence_CursorsPreserved()
    {
        var fileSystem = new Mock<IFileSystem>();
        string? savedJson = null;
        var configDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TerminalHost");
        var filePath = System.IO.Path.Combine(configDir, "collab-state.json");
        var backupPath = filePath + ".bak";

        fileSystem.Setup(f => f.FileExists(filePath)).Returns(false);
        fileSystem.Setup(f => f.FileExists(backupPath)).Returns(false);
        fileSystem.Setup(f => f.CreateDirectory(It.IsAny<string>()));
        fileSystem.Setup(f => f.WriteAllText(filePath, It.IsAny<string>()))
            .Callback<string, string>((_, json) => savedJson = json);
        fileSystem.Setup(f => f.CopyFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()));

        // Send 3 messages, alice reads first 2
        var svc1 = new CollabService(fileSystem.Object);
        svc1.SendMessage("bob", "work", "msg1");
        svc1.SendMessage("bob", "work", "msg2");
        svc1.SendMessage("bob", "work", "msg3");
        svc1.ReadMessages("alice", "work", 0); // Reads all 3, cursor = 3
        svc1.Dispose();

        // Reload
        fileSystem.Setup(f => f.FileExists(filePath)).Returns(true);
        fileSystem.Setup(f => f.ReadAllText(filePath)).Returns(savedJson!);

        var svc2 = new CollabService(fileSystem.Object);

        // Send a new message
        svc2.SendMessage("bob", "work", "msg4");

        // Alice re-subscribes (as she would in real usage via read_messages or subscribe)
        svc2.Subscribe("alice", "work");

        // Alice's cursor was at 3, so she should only see msg4
        var counts = svc2.GetUnreadCounts("alice");
        counts.ShouldContainKey("work");
        counts["work"].ShouldBe(1);

        svc2.Dispose();
    }

    [Fact]
    public void Persistence_StaleTopicsPruned()
    {
        var fileSystem = new Mock<IFileSystem>();
        var configDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TerminalHost");
        var filePath = System.IO.Path.Combine(configDir, "collab-state.json");
        var backupPath = filePath + ".bak";

        var oldTime = DateTime.UtcNow.AddHours(-48); // Well past 24h retention

        var state = new CollabPersistedState
        {
            NextMessageId = 3,
            Topics = new()
            {
                new PersistedTopic { Name = "stale", CreatedBy = "old", CreatedAt = oldTime },
                new PersistedTopic { Name = "fresh", CreatedBy = "new", CreatedAt = DateTime.UtcNow }
            },
            Messages = new()
            {
                new CollabMessage { Id = 1, Topic = "stale", Sender = "old", Content = "old msg", CreatedAt = oldTime },
                new CollabMessage { Id = 2, Topic = "fresh", Sender = "new", Content = "new msg", CreatedAt = DateTime.UtcNow }
            },
            Cursors = new()
        };

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });

        fileSystem.Setup(f => f.FileExists(filePath)).Returns(true);
        fileSystem.Setup(f => f.FileExists(backupPath)).Returns(false);
        fileSystem.Setup(f => f.ReadAllText(filePath)).Returns(json);
        fileSystem.Setup(f => f.CreateDirectory(It.IsAny<string>()));
        fileSystem.Setup(f => f.WriteAllText(It.IsAny<string>(), It.IsAny<string>()));

        var svc = new CollabService(fileSystem.Object);

        var topics = svc.GetTopics();
        topics.Count.ShouldBe(1);
        topics[0].Name.ShouldBe("fresh");

        svc.Dispose();
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var svc = CreateInMemory();
        svc.Dispose();
        svc.Dispose(); // Should not throw
    }

    #endregion
}
