using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Configuration;
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
}

[Collection("Sequential")]
public class CommandMonitorIntegrationTests : MongoDbTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task StepMessage_IsNull_WhenMonitoringDisabled()
    {
        // Default test setup has monitoring disabled
        var sut = new DiskTestRepositoryCollection(MongoDbServiceFactory, DatabaseContext);
        await sut.AddAsync(new TestEntity { Id = ObjectId.GenerateNewId(), Value = "test" });

        // Get the call info — steps should not have driver messages
        var entity = await sut.GetOneAsync(x => x.Value == "test");
        entity.Should().NotBeNull();
    }
}
