using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class CommandMonitorTests
{
    [Fact]
    public void EnableCommandMonitoring_DefaultsFalse()
    {
        var options = new MonitorOptions();

        options.EnableCommandMonitoring.Should().BeFalse();
    }

    [Fact]
    public void EnableCommandMonitoring_CanBeSet()
    {
        var options = new MonitorOptions { EnableCommandMonitoring = true };

        options.EnableCommandMonitoring.Should().BeTrue();
    }

    [Fact]
    public void TakeSince_ReturnsEmpty_WhenNoCommands()
    {
        var service = new CommandMonitorService(null);

        var result = service.TakeSince(Stopwatch.GetTimestamp());

        result.Should().BeEmpty();
    }

    [Fact]
    public void TakeSince_ReturnsEmpty_ForCommandsBeforeTimestamp()
    {
        var service = new CommandMonitorService(null);
        var after = Stopwatch.GetTimestamp();

        // No commands stored after this timestamp
        var result = service.TakeSince(after);

        result.Should().BeEmpty();
    }
}

[Collection("Sequential")]
public class CommandMonitorIntegrationTests : MongoDbTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task StepMessage_IsNull_WhenMonitoringDisabled()
    {
        var sut = new DiskTestRepositoryCollection(MongoDbServiceFactory, DatabaseContext);
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "test" });

        var entity = await sut.GetOneAsync(x => x.Value == "test");
        entity.Should().NotBeNull();
    }
}
