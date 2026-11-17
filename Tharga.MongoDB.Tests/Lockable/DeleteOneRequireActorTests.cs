using System.Threading.Tasks;
using AwesomeAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Lockable;
using Tharga.MongoDB.Tests.Lockable.Base;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable;

/// <summary>
/// Coverage for GitHub issue #197. DeleteOneAsync(id) passed no actor, which throws with the default RequireActor => true,
/// and committed a null scope (NullReferenceException) when the id did not exist.
/// </summary>
[Collection("Sequential")]
public class DeleteOneRequireActorTests : LockableTestBase
{
    // Keeps the default RequireActor => true, unlike LockableTestRepositoryCollection.
    private sealed class ActorRequiredCollection : LockableRepositoryCollectionBase<LockableTestEntity, ObjectId>
    {
        public ActorRequiredCollection(IMongoDbServiceFactory mongoDbServiceFactory)
            : base(mongoDbServiceFactory)
        {
        }
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DeleteOneAsync_DefaultRequireActor_ExistingId_DeletesAndReturnsEntity()
    {
        var sut = new ActorRequiredCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);

        var result = await sut.DeleteOneAsync(entity.Id);

        result.Should().Be(entity);
        (await sut.CountAsync(x => true)).Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DeleteOneAsync_DefaultRequireActor_MissingId_ReturnsNull()
    {
        var sut = new ActorRequiredCollection(_mongoDbServiceFactory);

        var result = await sut.DeleteOneAsync(ObjectId.GenerateNewId());

        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DeleteOneAsync_NoRequireActor_MissingId_ReturnsNull()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);

        var result = await sut.DeleteOneAsync(ObjectId.GenerateNewId());

        result.Should().BeNull();
    }
}
