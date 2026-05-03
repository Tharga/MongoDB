using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Lockable;
using Tharga.MongoDB.Tests.Lockable.Base;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable;

[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class LockTests : LockableTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task LockCommitUpdate()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await sut.AddAsync(entity);

        await using var scope = await sut.LockAsync(entity.Id);
        var updated = scope.Entity with { Data = "updated", Count = 1 };

        var committed = await scope.CommitAsync(CommitMode.Update, updated);

        committed.Data.Should().Be("updated");
        committed.Count.Should().Be(1);
        var post = await sut.GetOneAsync(entity.Id);
        post.Data.Should().Be("updated");
        post.Count.Should().Be(1);
        post.Lock.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockCommitUpdateNoEntity_CommitsOriginalUnchanged()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "initial" };
        await sut.AddAsync(entity);

        await using var scope = await sut.LockAsync(entity.Id);

        var committed = await scope.CommitAsync(CommitMode.Update);

        committed.Data.Should().Be("initial");
        var post = await sut.GetOneAsync(entity.Id);
        post.Data.Should().Be("initial");
        post.Lock.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockCommitDelete()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "to-delete" };
        await sut.AddAsync(entity);

        await using var scope = await sut.LockAsync(entity.Id);
        await scope.CommitAsync(CommitMode.Delete);

        (await sut.CountAsync(x => true)).Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockAbandon_LeavesEntityUnchangedAndUnlocked()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "original" };
        await sut.AddAsync(entity);

        var scope = await sut.LockAsync(entity.Id);
        await scope.AbandonAsync();

        var post = await sut.GetOneAsync(entity.Id);
        post.Data.Should().Be("original");
        post.Lock.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockDisposeWithoutCommit_ReleasesLock()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);

        // Lock and dispose without committing
        await (await sut.LockAsync(entity.Id)).DisposeAsync();

        // Should be lockable again
        await using var scope = await sut.LockAsync(entity.Id);
        scope.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockAlreadyLocked_ThrowsLockException()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);

        await using var first = await sut.LockAsync(entity.Id);

        Func<Task> act = async () => await sut.LockAsync(entity.Id, timeout: TimeSpan.FromMilliseconds(10));

        await act.Should().ThrowAsync<LockException>();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockSetErrorState_RecordsExceptionOnLock()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);

        var scope = await sut.LockAsync(entity.Id);
        await scope.SetErrorStateAsync(new InvalidOperationException("boom"));

        var post = await sut.GetOneAsync(entity.Id);
        post.Lock.Should().NotBeNull();
        post.Lock.ExceptionInfo.Should().NotBeNull();
        post.Lock.ExceptionInfo.Message.Should().Be("boom");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockCommitAfterRelease_Throws()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);

        var scope = await sut.LockAsync(entity.Id);
        await scope.AbandonAsync();

        Func<Task> act = async () => await scope.CommitAsync(CommitMode.Update);

        await act.Should().ThrowAsync<LockAlreadyReleasedException>();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockByFilter_LocksMatchingDocument()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "match" };
        await sut.AddAsync(entity);

        var filter = Builders<LockableTestEntity>.Filter.Eq(x => x.Data, "match");
        await using var scope = await sut.LockAsync(filter);

        scope.Entity.Id.Should().Be(entity.Id);
        await scope.CommitAsync(CommitMode.Update);

        (await sut.GetOneAsync(entity.Id)).Lock.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockByPredicate_LocksMatchingDocument()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "p-match" };
        await sut.AddAsync(entity);

        await using var scope = await sut.LockAsync(x => x.Data == "p-match");

        scope.Entity.Id.Should().Be(entity.Id);
        await scope.CommitAsync(CommitMode.Update);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockByFilter_NoMatch_ReturnsNull()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);

        var filter = Builders<LockableTestEntity>.Filter.Eq(x => x.Data, "nope");
        var scope = await sut.LockAsync(filter);

        scope.Should().BeNull();
    }

}
