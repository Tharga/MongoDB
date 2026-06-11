using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Tests.Lockable.Renewable.Base;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable.Renewable;

[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class RenewableExecuteAsyncGuardTests : RenewableLockableTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task OperationRead_Allowed()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);

        var count = await sut.ExecuteAsync(col => col.CountDocumentsAsync(FilterDefinition<LockableTestEntity>.Empty), Operation.Read);

        count.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task OperationCreate_Allowed()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };

        var result = await sut.ExecuteAsync(async col =>
        {
            await col.InsertOneAsync(entity);
            return true;
        }, Operation.Create);

        result.Should().BeTrue();
        (await sut.GetOneAsync(entity.Id)).Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task OperationUpdate_Throws()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);

        var act = () => sut.ExecuteAsync(col => Task.FromResult(true), Operation.Update);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain(nameof(Operation.Update));
        ex.Which.Message.Should().Contain("lock-acquire/commit cycle");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task OperationDelete_Throws()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);

        var act = () => sut.ExecuteAsync(col => Task.FromResult(true), Operation.Delete);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain(nameof(Operation.Delete));
        ex.Which.Message.Should().Contain("lock-acquire/commit cycle");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task OperationUpdate_WithCancellationToken_Throws()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);

        var act = () => sut.ExecuteAsync((col, _) => Task.FromResult(true), Operation.Update, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task IndexCreate_OperationCreate_Works()
    {
        var sut = new RenewableLockableTestRepositoryCollection(_mongoDbServiceFactory);
        const string indexName = "Data_Unique_Idx";

        var ok = await sut.ExecuteAsync(async col =>
        {
            await col.Indexes.CreateOneAsync(new CreateIndexModel<LockableTestEntity>(
                Builders<LockableTestEntity>.IndexKeys.Ascending(x => x.Data),
                new CreateIndexOptions { Unique = true, Name = indexName }));
            return true;
        }, Operation.Create);

        ok.Should().BeTrue();

        var indexes = await sut.ExecuteAsync(col => col.Indexes.List().ToListAsync(), Operation.Read);
        indexes.Select(i => i["name"].AsString).Should().Contain(indexName);
    }
}
