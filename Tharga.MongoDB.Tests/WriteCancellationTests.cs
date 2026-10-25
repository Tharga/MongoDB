using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

[Collection("Sequential")]
public class WriteCancellationTests : GenericRepositoryCollectionBaseTestBase
{
    public WriteCancellationTests()
    {
        Prepare([TestEntityFactory.CreateTestEntity, TestEntityFactory.CreateTestEntity]);
    }

    public static TheoryData<string> Operations =>
    [
        "AddAsync", "TryAddAsync", "AddManyAsync", "AddOrReplaceAsync",
        "ReplaceOneAsync", "ReplaceOneAsync(filter)",
        "UpdateOneAsync(id)", "UpdateOneAsync(predicate)", "UpdateOneAsync(filter)",
        "UpdateManyAsync(predicate)", "UpdateManyAsync(filter)",
        "DeleteOneAsync(id)", "DeleteOneAsync(predicate)", "DeleteOneAsync(filter)",
        "DeleteManyAsync(predicate)", "DeleteManyAsync(filter)",
    ];

    [Theory]
    [MemberData(nameof(Operations))]
    [Trait("Category", "Database")]
    public async Task Write_WithCancelledToken_ThrowsAndWritesNothing(string operation)
    {
        //Arrange
        var sut = await GetCollection();
        var existing = InitialData.First();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        //Act
        var act = () => InvokeAsync(operation, sut, existing, cancellation.Token);

        //Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        var stored = await sut.GetAsync(x => true).ToArrayAsync();
        stored.OrderBy(x => x.Id).Should().Equal(InitialData.OrderBy(x => x.Id));
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task UpdateOneAsync_PredicateWithOptions_UsesTheOptions()
    {
        //Arrange
        var sut = await GetCollection();
        var shared = Guid.NewGuid().ToString();
        await sut.AddManyAsync([NewEntity() with { ExtraValue = shared }, NewEntity() with { ExtraValue = shared }]);
        var updated = Guid.NewGuid().ToString();
        var update = Builders<TestEntity>.Update.Set(x => x.Value, updated);

        //Act
        var act = () => sut.UpdateOneAsync(x => x.ExtraValue == shared, update, new OneOption<TestEntity> { Mode = EMode.FirstOrDefault });

        //Assert
        await act.Should().NotThrowAsync("FirstOrDefault picks one of the two matches instead of requiring a single one");
        (await sut.GetAsync(x => x.ExtraValue == shared && x.Value == updated).ToArrayAsync()).Should().HaveCount(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(EMode.FirstOrDefault)]
    [Trait("Category", "Database")]
    public async Task UpdateOneAsync_TokenCancelledAfterTheUpdate_AfterCanStillBeRead(EMode? mode)
    {
        //Arrange
        var sut = await GetCollection();
        var existing = InitialData.First();
        var updated = Guid.NewGuid().ToString();
        var update = Builders<TestEntity>.Update.Set(x => x.ExtraValue, updated);
        var options = mode == null ? null : new OneOption<TestEntity> { Mode = mode.Value };
        using var cancellation = new CancellationTokenSource();
        var result = await sut.UpdateOneAsync(Builders<TestEntity>.Filter.Eq(x => x.Id, existing.Id), update, options, cancellationToken: cancellation.Token);
        await cancellation.CancelAsync();

        //Act
        var after = await result.GetAfterAsync();

        //Assert
        after.ExtraValue.Should().Be(updated, "the update completed before the token was cancelled");
    }

    private static Task InvokeAsync(string operation, DiskRepositoryCollectionBase<TestEntity, ObjectId> sut, TestEntity existing, CancellationToken cancellationToken)
    {
        var update = Builders<TestEntity>.Update.Set(x => x.Value, "changed");
        var byId = Builders<TestEntity>.Filter.Eq(x => x.Id, existing.Id);
        var replacement = existing with { Value = "changed" };

        return operation switch
        {
            "AddAsync" => sut.AddAsync(NewEntity(), cancellationToken: cancellationToken),
            "TryAddAsync" => sut.TryAddAsync(NewEntity(), cancellationToken: cancellationToken),
            "AddManyAsync" => sut.AddManyAsync([NewEntity()], cancellationToken: cancellationToken),
            "AddOrReplaceAsync" => sut.AddOrReplaceAsync(replacement, cancellationToken: cancellationToken),
            "ReplaceOneAsync" => sut.ReplaceOneAsync(replacement, cancellationToken: cancellationToken),
            "ReplaceOneAsync(filter)" => sut.ReplaceOneAsync(replacement, byId, cancellationToken: cancellationToken),
            "UpdateOneAsync(id)" => sut.UpdateOneAsync(existing.Id, update, cancellationToken: cancellationToken),
            "UpdateOneAsync(predicate)" => sut.UpdateOneAsync(x => x.Id == existing.Id, update, cancellationToken: cancellationToken),
            "UpdateOneAsync(filter)" => sut.UpdateOneAsync(byId, update, cancellationToken: cancellationToken),
            "UpdateManyAsync(predicate)" => sut.UpdateManyAsync(x => true, update, cancellationToken: cancellationToken),
            "UpdateManyAsync(filter)" => sut.UpdateManyAsync(FilterDefinition<TestEntity>.Empty, update, cancellationToken: cancellationToken),
            "DeleteOneAsync(id)" => sut.DeleteOneAsync(existing.Id, cancellationToken: cancellationToken),
            "DeleteOneAsync(predicate)" => sut.DeleteOneAsync(x => x.Id == existing.Id, cancellationToken: cancellationToken),
            "DeleteOneAsync(filter)" => sut.DeleteOneAsync(byId, cancellationToken: cancellationToken),
            "DeleteManyAsync(predicate)" => sut.DeleteManyAsync(x => true, cancellationToken: cancellationToken),
            "DeleteManyAsync(filter)" => sut.DeleteManyAsync(FilterDefinition<TestEntity>.Empty, cancellationToken: cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };
    }

    private static TestEntity NewEntity()
    {
        return TestEntityFactory.CreateTestEntity();
    }
}
