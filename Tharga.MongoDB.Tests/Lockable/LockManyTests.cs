using System;
using System.Collections.Generic;
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
public class LockManyTests : LockableTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task LockManyMixedDecisions_AppliesEachAndReturnsSummary()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var a = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "a" };
        var b = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "b" };
        var c = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "c" };
        await sut.AddAsync(a);
        await sut.AddAsync(b);
        await sut.AddAsync(c);

        await using var lease = await sut.LockManyAsync([a.Id, b.Id, c.Id]);

        // Update a
        var updatedA = lease.Documents.Single(x => x.Id == a.Id) with { Data = "a-updated" };
        lease.MarkForUpdate(updatedA);
        // Delete b
        lease.MarkForDelete(b.Id);
        // c left unmarked → released unchanged

        var summary = await lease.CommitAsync();

        summary.Updated.Should().Be(1);
        summary.Deleted.Should().Be(1);
        summary.ReleasedUnchanged.Should().Be(1);
        summary.Failures.Should().BeEmpty();

        (await sut.GetOneAsync(a.Id)).Data.Should().Be("a-updated");
        (await sut.GetOneAsync(b.Id)).Should().BeNull();
        var postC = await sut.GetOneAsync(c.Id);
        postC.Data.Should().Be("c");
        postC.Lock.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockManyAcquireFailure_RollsBackPartialLocks()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var a = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        var b = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        var c = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(a);
        await sut.AddAsync(b);
        await sut.AddAsync(c);

        // Pre-lock one of them so the LockManyAsync mid-acquire fails.
        await using var blocker = await sut.LockAsync(b.Id);

        Func<Task> act = async () => await sut.LockManyAsync(new[] { a.Id, b.Id, c.Id }, timeout: TimeSpan.FromMilliseconds(10));

        await act.Should().ThrowAsync<LockException>();

        // Other two must be lockable again — partial lock on whichever was acquired first should have rolled back.
        await using var sa = await sut.LockAsync(a.Id, timeout: TimeSpan.FromSeconds(1));
        await using var sc = await sut.LockAsync(c.Id, timeout: TimeSpan.FromSeconds(1));
        sa.Should().NotBeNull();
        sc.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockManyEmptyIdList_ReturnsEmptyLease()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);

        await using var lease = await sut.LockManyAsync(Array.Empty<ObjectId>());

        lease.Documents.Should().BeEmpty();
        var summary = await lease.CommitAsync();
        summary.Updated.Should().Be(0);
        summary.Deleted.Should().Be(0);
        summary.ReleasedUnchanged.Should().Be(0);
        summary.Failures.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockManyMarkUnknownId_ThrowsArgumentException()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var a = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(a);

        await using var lease = await sut.LockManyAsync([a.Id]);

        Action act = () => lease.MarkForDelete(ObjectId.GenerateNewId());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockManyDisposeWithoutCommit_ReleasesAllLocks()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var a = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        var b = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(a);
        await sut.AddAsync(b);

        await (await sut.LockManyAsync([a.Id, b.Id])).DisposeAsync();

        // Both should be lockable again.
        await using var sa = await sut.LockAsync(a.Id, timeout: TimeSpan.FromSeconds(1));
        await using var sb = await sut.LockAsync(b.Id, timeout: TimeSpan.FromSeconds(1));
        sa.Should().NotBeNull();
        sb.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockManyByFilter_LocksMatchingDocuments()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var a = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "group" };
        var b = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "group" };
        var c = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "other" };
        await sut.AddAsync(a);
        await sut.AddAsync(b);
        await sut.AddAsync(c);

        var filter = Builders<LockableTestEntity>.Filter.Eq(x => x.Data, "group");
        await using var lease = await sut.LockManyAsync(filter);

        lease.Documents.Should().HaveCount(2);
        lease.Documents.Select(x => x.Id).Should().BeEquivalentTo(new[] { a.Id, b.Id });
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockManyByPredicate_LocksMatchingDocuments()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var a = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Count = 5 };
        var b = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Count = 5 };
        var c = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Count = 1 };
        await sut.AddAsync(a);
        await sut.AddAsync(b);
        await sut.AddAsync(c);

        await using var lease = await sut.LockManyAsync(x => x.Count == 5);

        lease.Documents.Should().HaveCount(2);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockManyRemarkSameId_LastDecisionWins()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var a = new LockableTestEntity { Id = ObjectId.GenerateNewId(), Data = "a" };
        await sut.AddAsync(a);

        await using var lease = await sut.LockManyAsync([a.Id]);

        // First mark Update, then re-mark as Delete — Delete should win.
        var updatedA = lease.Documents.Single() with { Data = "should-not-stick" };
        lease.MarkForUpdate(updatedA);
        lease.MarkForDelete(a.Id);

        var summary = await lease.CommitAsync();

        summary.Deleted.Should().Be(1);
        summary.Updated.Should().Be(0);
        (await sut.GetOneAsync(a.Id)).Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockManyCommitTwice_Throws()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var a = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(a);

        await using var lease = await sut.LockManyAsync([a.Id]);
        await lease.CommitAsync();

        Func<Task> act = async () => await lease.CommitAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
