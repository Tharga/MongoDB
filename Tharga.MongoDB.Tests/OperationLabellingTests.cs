using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// The function name every operation reports to the monitor, the call history and the MCP call
/// surfaces. It is a display and grouping key — `DatabaseMonitor` aggregates call statistics by it —
/// so an operation reporting another operation's name silently merges two different things in every
/// one of those views.
/// </summary>
[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class OperationLabellingTests : MongoDbTestBase
{
    private DiskTestRepositoryCollection Collection => new(MongoDbServiceFactory, DatabaseContext);

    private List<(string FunctionName, Operation Operation)> RecordCalls()
    {
        var calls = new List<(string, Operation)>();
        MongoDbServiceFactory.CallStartEvent += (_, e) => calls.Add((e.FunctionName, e.Operation));
        return calls;
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DeleteOneAsync_ReportsItsOwnName()
    {
        // Regression: this overload reported nameof(UpdateOneAsync), so every delete showed up in the
        // monitor as an update. The Operation enum was always correct — only the label was wrong.
        var sut = Collection;
        await sut.AddAsync(TestEntityFactory.CreateTestEntity());
        var calls = RecordCalls();

        await sut.DeleteOneAsync(x => true);

        calls.Should().Contain(x => x.FunctionName == nameof(DiskTestRepositoryCollection.DeleteOneAsync));
        calls.Should().NotContain(x => x.FunctionName == nameof(DiskTestRepositoryCollection.UpdateOneAsync));
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DeleteOneAsync_ById_ReportsItsOwnName()
    {
        var sut = Collection;
        var entity = TestEntityFactory.CreateTestEntity();
        await sut.AddAsync(entity);
        var calls = RecordCalls();

        await sut.DeleteOneAsync(entity.Id);

        calls.Should().Contain(x => x.FunctionName == nameof(DiskTestRepositoryCollection.DeleteOneAsync));
        calls.Should().NotContain(x => x.FunctionName == nameof(DiskTestRepositoryCollection.UpdateOneAsync));
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DeleteOneAsync_ReportsTheDeleteOperation()
    {
        var sut = Collection;
        await sut.AddAsync(TestEntityFactory.CreateTestEntity());
        var calls = RecordCalls();

        await sut.DeleteOneAsync(x => true);

        calls.Should().Contain(x => x.Operation == Operation.Delete);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task UpdateOneAsync_StillReportsUpdate()
    {
        // The other user of nameof(UpdateOneAsync) is the genuine one — guards against the fix
        // being applied to the wrong call site.
        var sut = Collection;
        var entity = TestEntityFactory.CreateTestEntity();
        await sut.AddAsync(entity);
        var calls = RecordCalls();

        await sut.UpdateOneAsync(entity.Id, Builders<TestEntity>.Update.Set(x => x.Value, "changed"));

        calls.Should().Contain(x => x.FunctionName == nameof(DiskTestRepositoryCollection.UpdateOneAsync) && x.Operation == Operation.Update);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DeleteManyAsync_ReportsItsOwnName()
    {
        var sut = Collection;
        await sut.AddAsync(TestEntityFactory.CreateTestEntity());
        var calls = RecordCalls();

        await sut.DeleteManyAsync(x => true);

        calls.Should().Contain(x => x.FunctionName == nameof(DiskTestRepositoryCollection.DeleteManyAsync) && x.Operation == Operation.Delete);
    }
}
