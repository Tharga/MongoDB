using System;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class IngestCollectionInfoTests
{
    [Fact]
    public void IngestedCollectionInfo_PreservesOriginalRegistration()
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
        Enum.TryParse<Registration>(dto.Registration, out var registration);
        var info = new CollectionInfo
        {
            ConfigurationName = dto.ConfigurationName,
            DatabaseName = dto.DatabaseName,
            CollectionName = dto.CollectionName,
            Server = dto.Server,
            Discovery = discovery,
            Registration = registration,
            EntityTypes = dto.EntityTypes ?? [],
            CollectionType = null,
            Stats = dto.Stats,
        };

        // Assert — registration is preserved, not overwritten to Remote
        info.Registration.Should().Be(Registration.Static);
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
            Registration = Registration.Static,
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
            Registration = Registration.Static,
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

    [Fact]
    public void SourceTracking_MultipleSources_SameCollection()
    {
        // Arrange
        var sources = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<string, bool>>();
        var key = "Default.Db.Orders";

        // Act — two sources report the same collection
        var bag1 = sources.GetOrAdd(key, _ => new System.Collections.Concurrent.ConcurrentDictionary<string, bool>());
        bag1["Agent-A/OrderService"] = true;

        var bag2 = sources.GetOrAdd(key, _ => new System.Collections.Concurrent.ConcurrentDictionary<string, bool>());
        bag2["Agent-B/PaymentService"] = true;

        // Assert
        sources[key].Keys.Should().HaveCount(2);
        sources[key].Keys.Should().Contain("Agent-A/OrderService");
        sources[key].Keys.Should().Contain("Agent-B/PaymentService");
    }
}
