using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Interception;
using Tharga.MongoDB.Tests.Lockable.Base;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Interception;

/// <summary>
/// Coverage verification for the lockable family. <c>LockableRepositoryCollectionBase</c> delegates
/// every data operation — including lock acquire, commit, release and extend — to an inner
/// <c>Disk</c> collection, so interception needs no lockable-specific call sites. These tests exist
/// because that claim is exactly the kind that reads as protection while leaving holes: they drive
/// real lock cycles and assert the underlying disk operations were seen.
/// </summary>
[Collection("Sequential")]
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class LockableInterceptionCoverageTests : LockableTestBase
{
    private RecordingInterceptor UseRecorder()
    {
        var interceptor = new RecordingInterceptor();
        _mongoDbServiceFactory.Interceptors = [interceptor];
        return interceptor;
    }

    private void UseInterceptors(params ICollectionInterceptor[] interceptors)
    {
        _mongoDbServiceFactory.Interceptors = interceptors;
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockAcquireAndCommit_AreIntercepted()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        await sut.AddAsync(new LockableTestEntity { Count = 1 });
        var entity = await sut.GetOneAsync(x => true);
        var interceptor = UseRecorder();

        var scope = await sut.PickForUpdateAsync(entity.Id);
        var afterPick = interceptor.Operations.ToArray();

        await scope.CommitAsync(scope.Entity with { Count = 2 });
        var afterCommit = interceptor.Operations.Skip(afterPick.Length).ToArray();

        afterPick.Should().NotBeEmpty("acquiring a lock is a database write and must be intercepted");
        afterPick.Should().Contain("UpdateOneAsync", "the lock is taken with an atomic LockKey-guarded update");
        afterCommit.Should().NotBeEmpty("committing writes the entity back and must be intercepted");
        afterCommit.Should().Contain("ReplaceOneWithCheckAsync", "the commit routes through the checked-replace path");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockRelease_IsIntercepted()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        await sut.AddAsync(new LockableTestEntity { Count = 1 });
        var entity = await sut.GetOneAsync(x => true);
        var scope = await sut.PickForUpdateAsync(entity.Id);
        var interceptor = UseRecorder();

        await scope.AbandonAsync();

        interceptor.Operations.Should().NotBeEmpty("releasing a lock writes to the database");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task ExtendLock_IsIntercepted()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        await sut.AddAsync(new LockableTestEntity { Count = 1 });
        var entity = await sut.GetOneAsync(x => true);
        var scope = await sut.PickForUpdateAsync(entity.Id, TimeSpan.FromSeconds(30));
        var interceptor = UseRecorder();

        await scope.ExtendLockAsync(TimeSpan.FromSeconds(60), force: true);

        interceptor.Operations.Should().Contain("UpdateOneAsync", "extending a lease is a guarded write");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task PickForDelete_AndCommit_AreIntercepted()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        await sut.AddAsync(new LockableTestEntity { Count = 1 });
        var entity = await sut.GetOneAsync(x => true);
        var interceptor = UseRecorder();

        var scope = await sut.PickForDeleteAsync(entity.Id);
        await scope.CommitAsync();

        // Asserted on OperationType, not on the name. DeleteOneAsync(FilterDefinition, ...) labels
        // itself nameof(UpdateOneAsync) — a pre-existing copy-paste bug in the package that predates
        // this feature and also mislabels the monitor. It passes Operation.Delete correctly, so the
        // classification an interceptor should key on is right; only the display string is wrong.
        // Recorded as a follow-up rather than fixed here, because correcting it changes what the
        // monitor shows for every delete.
        interceptor.Calls.Should().Contain(x => x.OperationType == Operation.Delete,
            "the delete commit removes the document");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task Rejection_BlocksALockablePick()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        await sut.AddAsync(new LockableTestEntity { Count = 1 });
        var entity = await sut.GetOneAsync(x => true);
        UseInterceptors(new RejectingInterceptor());

        var act = async () => await sut.PickForUpdateAsync(entity.Id);

        await act.Should().ThrowAsync<CollectionAccessDeniedException>();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task GetUnlockedAsync_FiresAtInvocation_WithoutEnumerating()
    {
        // This one was an iterator before the seam landed, so its Disk.GetAsync call was deferred to
        // enumeration. It is now a pass-through, which is what makes invocation-time firing work.
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        await sut.AddAsync(new LockableTestEntity { Count = 1 });
        var interceptor = UseRecorder();

        _ = sut.GetUnlockedAsync(x => true);

        interceptor.Operations.Should().ContainSingle().Which.Should().Be("GetAsync");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockableOperations_ReportDiskLevelNames()
    {
        // Settled design decision: a lockable call reports the underlying disk operation, not the
        // semantic wrapper. Recorded here so a future change to that decision fails loudly.
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        await sut.AddAsync(new LockableTestEntity { Count = 1 });
        var entity = await sut.GetOneAsync(x => true);
        var interceptor = UseRecorder();

        var scope = await sut.PickForUpdateAsync(entity.Id);
        await scope.AbandonAsync();

        interceptor.Operations.Should().NotContain("PickForUpdateAsync");
        interceptor.Operations.Should().OnlyContain(x => x.EndsWith("Async"));
    }

    private class RecordingInterceptor : ICollectionInterceptor
    {
        private readonly List<CollectionCallInfo> _calls = [];

        public IReadOnlyList<CollectionCallInfo> Calls
        {
            get { lock (_calls) return _calls.ToArray(); }
        }

        public IReadOnlyList<string> Operations => Calls.Select(x => x.Operation).ToArray();

        public ValueTask<InterceptDecision> BeforeCallAsync(CollectionCallInfo call, CancellationToken cancellationToken = default)
        {
            lock (_calls) _calls.Add(call);
            return ValueTask.FromResult(InterceptDecision.Proceed);
        }
    }

    private class RejectingInterceptor : ICollectionInterceptor
    {
        public ValueTask<InterceptDecision> BeforeCallAsync(CollectionCallInfo call, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(InterceptDecision.Reject("denied"));
        }
    }
}
