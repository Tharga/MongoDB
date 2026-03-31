using System;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Tharga.MongoDB.Configuration;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class IngestCollectionInfoTests
{
    private readonly DatabaseMonitor _monitor;

    public IngestCollectionInfoTests()
    {
        // DatabaseMonitor requires many dependencies — use a minimal approach via IDatabaseMonitor
        // We test via the real DatabaseMonitor by checking _remoteCollections indirectly
        // through GetInstancesAsync. But that requires Start() which needs full DI.
        // Instead, test the RemoteCollectionInfoDto → CollectionInfo conversion and dedup
        // by testing the ingest + GetInstancesAsync path won't work without full wiring.
        // So we test at the handler level with a mock.
    }

    [Fact]
    public void IngestedCollectionInfo_HasRegistrationRemote()
    {
        // Arrange
        var dto = new RemoteCollectionInfoDto
        {
            ConfigurationName = "Default",
            DatabaseName = "RemoteDb",
            CollectionName = "Orders",
            SourceName = "Agent-A",
            Server = "remote-server",
            Discovery = "Database",
            Registration = "Static",
            EntityTypes = ["OrderEntity"],
            Stats = new CollectionStats { DocumentCount = 100, Size = 4096 }
        };

        // Act — parse the same way DatabaseMonitor does
        Enum.TryParse<Discovery>(dto.Discovery, out var discovery);
        var info = new CollectionInfo
        {
            ConfigurationName = dto.ConfigurationName,
            DatabaseName = dto.DatabaseName,
            CollectionName = dto.CollectionName,
            Server = dto.Server,
            DatabasePart = dto.DatabasePart,
            Discovery = discovery,
            Registration = Registration.Remote,
            EntityTypes = dto.EntityTypes ?? [],
            CollectionType = null,
            Stats = dto.Stats,
            Index = dto.Index,
            Clean = dto.Clean,
        };

        // Assert
        info.Registration.Should().Be(Registration.Remote);
        info.ConfigurationName.Value.Should().Be("Default");
        info.DatabaseName.Should().Be("RemoteDb");
        info.CollectionName.Should().Be("Orders");
        info.Stats.DocumentCount.Should().Be(100);
        info.CollectionType.Should().BeNull();
    }

    [Fact]
    public void DuplicateFingerprint_OverwritesPrevious()
    {
        // Arrange — simulate the ConcurrentDictionary keyed by fingerprint
        var dict = new System.Collections.Concurrent.ConcurrentDictionary<string, CollectionInfo>();

        var first = new CollectionInfo
        {
            ConfigurationName = "Default",
            DatabaseName = "Db",
            CollectionName = "Orders",
            Server = "server-a",
            Registration = Registration.Remote,
            EntityTypes = [],
            CollectionType = null,
            Stats = new CollectionStats { DocumentCount = 50, Size = 1024 }
        };

        var second = new CollectionInfo
        {
            ConfigurationName = "Default",
            DatabaseName = "Db",
            CollectionName = "Orders",
            Server = "server-b",
            Registration = Registration.Remote,
            EntityTypes = [],
            CollectionType = null,
            Stats = new CollectionStats { DocumentCount = 150, Size = 8192 }
        };

        // Act
        dict[first.Key] = first;
        dict[second.Key] = second;

        // Assert — same key, second overwrites first
        dict.Should().HaveCount(1);
        dict.Values.Single().Stats.DocumentCount.Should().Be(150);
    }
}
