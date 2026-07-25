using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Interception;
using Tharga.MongoDB.Internals;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Interception;

[Collection("Sequential")]
public class InterceptorPipelineTests : MongoDbTestBase
{
    private DiskTestRepositoryCollection Collection => new(MongoDbServiceFactory, DatabaseContext);

    private void UseInterceptors(params ICollectionInterceptor[] interceptors)
    {
        ((MongoDbServiceFactory)MongoDbServiceFactory).Interceptors = interceptors;
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task ExecuteAsyncOperation_FiresInterceptor()
    {
        var interceptor = new RecordingInterceptor();
        UseInterceptors(interceptor);

        await Collection.CountAsync(x => true);

        var call = interceptor.Calls.Should().ContainSingle().Subject;
        call.Operation.Should().Be("CountAsync");
        call.OperationType.Should().Be(Operation.Read);
        call.CollectionName.Should().Be("Test");
        call.EntityType.Should().Be<TestEntity>();
        call.Point.Should().Be(InterceptionPoint.Invocation);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task WriteOperation_ReportsWriteOperationType()
    {
        var interceptor = new RecordingInterceptor();
        UseInterceptors(interceptor);

        await Collection.AddAsync(TestEntityFactory.CreateTestEntity());

        interceptor.Calls.Should().ContainSingle()
            .Which.OperationType.Should().Be(Operation.Create);
    }

    [Fact]
    [Trait("Category", "Database")]
    public void StreamingOperation_FiresAtInvocation_WithoutEnumerating()
    {
        // The whole point of the iterator-deferral rework. As an `async IAsyncEnumerable` method,
        // GetAsync deferred its body until the first MoveNextAsync — so an authorization gate would
        // have run at enumeration time, after the caller's ambient context was gone.
        var interceptor = new RecordingInterceptor();
        UseInterceptors(interceptor);

        _ = Collection.GetAsync(x => true);

        interceptor.Calls.Should().ContainSingle()
            .Which.Operation.Should().Be("GetAsync");
    }

    [Fact]
    [Trait("Category", "Database")]
    public void ProjectionStream_FiresAtInvocation_WithoutEnumerating()
    {
        var interceptor = new RecordingInterceptor();
        UseInterceptors(interceptor);

        _ = Collection.GetProjectionAsync<TestProjectionEntity>(x => true);

        interceptor.Calls.Should().ContainSingle()
            .Which.Operation.Should().Be("GetProjectionAsync");
    }

    [Fact]
    [Trait("Category", "Database")]
    public void GetDirtyAsync_FiresOnceAtInvocation()
    {
        // Reports as GetDirtyAsync exactly once — the inner scan goes straight to the iterator so it
        // does not surface a second, nested GetAsync at enumeration time.
        var interceptor = new RecordingInterceptor();
        UseInterceptors(interceptor);

        _ = Collection.GetDirtyAsync();

        interceptor.Calls.Should().ContainSingle()
            .Which.Operation.Should().Be("GetDirtyAsync");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DropCollectionAsync_FiresInterceptor()
    {
        var interceptor = new RecordingInterceptor();
        UseInterceptors(interceptor);

        await Collection.DropCollectionAsync();

        var call = interceptor.Calls.Should().ContainSingle().Subject;
        call.Operation.Should().Be("DropCollectionAsync");
        call.OperationType.Should().Be(Operation.Delete);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task Rejection_PreventsTheOperation_AndSurfacesReason()
    {
        var sut = Collection;
        await sut.AddAsync(TestEntityFactory.CreateTestEntity());
        UseInterceptors(new RejectingInterceptor("no team scope"));

        var act = async () => await sut.CountAsync(x => true);

        var exception = await act.Should().ThrowAsync<CollectionAccessDeniedException>();
        exception.Which.Reason.Should().Be("no team scope");
        exception.Which.Call.Operation.Should().Be("CountAsync");
    }

    [Fact]
    [Trait("Category", "Database")]
    public void Rejection_OnStreamingOperation_ThrowsAtTheCallSite()
    {
        // A synchronous interceptor completes the chain synchronously, so the caller learns at the
        // call site rather than at first enumeration.
        UseInterceptors(new RejectingInterceptor("denied"));
        var sut = Collection;

        Action act = () => sut.GetAsync(x => true);

        act.Should().Throw<CollectionAccessDeniedException>();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task InterceptorThrow_PropagatesUnchanged()
    {
        UseInterceptors(new ThrowingInterceptor());
        var sut = Collection;

        var act = async () => await sut.CountAsync(x => true);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("domain rule violated");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task Interceptors_RunInOrder_AndRejectionShortCircuits()
    {
        var first = new RecordingInterceptor();
        var rejecting = new RejectingInterceptor("stop here");
        var never = new RecordingInterceptor();
        UseInterceptors(first, rejecting, never);
        var sut = Collection;

        var act = async () => await sut.CountAsync(x => true);

        await act.Should().ThrowAsync<CollectionAccessDeniedException>();
        first.Calls.Should().ContainSingle("interceptors before the rejection still run");
        never.Calls.Should().BeEmpty("a rejection short-circuits the rest of the chain");
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task RejectedCall_IsInvisibleToTheMonitor()
    {
        // Design decision: a rejected call never touched the database, so it must not be reported as
        // a database call that happened. The chain therefore runs before FireCallStartEvent.
        var started = 0;
        MongoDbServiceFactory.CallStartEvent += (_, _) => Interlocked.Increment(ref started);
        UseInterceptors(new RejectingInterceptor("denied"));
        var sut = Collection;

        var act = async () => await sut.CountAsync(x => true);

        await act.Should().ThrowAsync<CollectionAccessDeniedException>();
        started.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task EnumerationOnlyInterceptor_DoesNotFireAtInvocation()
    {
        var interceptor = new RecordingInterceptor { DeclaredPoints = InterceptionPoint.Enumeration };
        UseInterceptors(interceptor);

        _ = Collection.GetAsync(x => true);

        interceptor.Calls.Should().BeEmpty("this interceptor asked only for the enumeration point");
        await Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task AsyncInterceptor_StillGatesTheOperation()
    {
        // An interceptor that genuinely yields cannot complete synchronously, so the rejection
        // surfaces when the stream is enumerated rather than at the call site. The operation must
        // still never reach the database.
        UseInterceptors(new AsyncRejectingInterceptor());
        var sut = Collection;

        var act = async () => await sut.GetAsync(x => true).ToArrayAsync();

        await act.Should().ThrowAsync<CollectionAccessDeniedException>();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task NoInterceptors_OperationsBehaveNormally()
    {
        UseInterceptors();
        var sut = Collection;

        await sut.AddAsync(TestEntityFactory.CreateTestEntity());
        var count = await sut.CountAsync(x => true);
        var items = await sut.GetAsync(x => true).ToArrayAsync();

        count.Should().Be(1);
        items.Should().ContainSingle();
    }

    private class RecordingInterceptor : ICollectionInterceptor
    {
        public List<CollectionCallInfo> Calls { get; } = [];
        public InterceptionPoint DeclaredPoints { get; init; } = InterceptionPoint.Invocation;
        public InterceptionPoint Points => DeclaredPoints;

        public ValueTask<InterceptDecision> BeforeCallAsync(CollectionCallInfo call, CancellationToken cancellationToken = default)
        {
            Calls.Add(call);
            return ValueTask.FromResult(InterceptDecision.Proceed);
        }
    }

    private class RejectingInterceptor(string reason) : ICollectionInterceptor
    {
        public ValueTask<InterceptDecision> BeforeCallAsync(CollectionCallInfo call, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(InterceptDecision.Reject(reason));
        }
    }

    private class AsyncRejectingInterceptor : ICollectionInterceptor
    {
        public async ValueTask<InterceptDecision> BeforeCallAsync(CollectionCallInfo call, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return InterceptDecision.Reject("async denial");
        }
    }

    private class ThrowingInterceptor : ICollectionInterceptor
    {
        public ValueTask<InterceptDecision> BeforeCallAsync(CollectionCallInfo call, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("domain rule violated");
        }
    }
}
