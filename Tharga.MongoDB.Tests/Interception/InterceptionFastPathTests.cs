using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;
using Moq.AutoMock;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Interception;
using Tharga.MongoDB.Internals;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Interception;

/// <summary>
/// Pins the cost of the seam for consumers who register nothing — which is every existing consumer.
/// The interception path must be a field read and a branch: no allocation, and in particular no
/// <c>CollectionCallInfo</c> built speculatively. These assert exact zero rather than a threshold,
/// because the path is straight-line code with no legitimate reason to allocate at all; a
/// non-zero result means something was added to it.
/// </summary>
/// <remarks>
/// Deliberately does NOT derive from <c>MongoDbTestBase</c>. Nothing here touches the database — the
/// interception path is measured before any driver work — and that base class drops its database on
/// dispose, which would make these tests require a live mongod and be excluded from CI by the
/// <c>Category!=Database</c> filter. CI is exactly where an accidental allocation should be caught,
/// so the factory is built here without the disposal.
/// </remarks>
public class InterceptionFastPathTests
{
    private const int WarmupIterations = 200;
    private const int MeasuredIterations = 1000;

    private readonly MongoDbServiceFactory _factory;
    private readonly DatabaseContext _databaseContext;

    public InterceptionFastPathTests()
    {
        var mocker = new AutoMocker(MockBehavior.Strict);

        _databaseContext = Mock.Of<DatabaseContext>(x => x.DatabasePart == Guid.NewGuid().ToString() && x.ConfigurationName == "Default");

        var configurationMock = new Mock<IRepositoryConfigurationInternal>(MockBehavior.Strict);
        configurationMock.Setup(x => x.GetDatabaseUrl()).Returns(() => new MongoUrl($"mongodb://localhost:27017/Tharga_MongoDb_FastPath_{_databaseContext.DatabasePart}"));
        configurationMock.Setup(x => x.GetConfiguration()).Returns(Mock.Of<MongoDbConfig>(x => x.FetchSize == 100));
        configurationMock.Setup(x => x.GetAssureIndexMode()).Returns(AssureIndexMode.ByName);
        configurationMock.Setup(x => x.GetConfigurationName()).Returns("Default");
        configurationMock.Setup(x => x.GetDatabaseContext()).Returns(Mock.Of<DatabaseContext>());

        var configurationLoaderMock = new Mock<IRepositoryConfigurationLoader>(MockBehavior.Strict);
        configurationLoaderMock.Setup(x => x.GetConfiguration(It.IsAny<Func<DatabaseContext>>())).Returns(configurationMock.Object);
        mocker.Use(configurationLoaderMock);

        var mongoDbClientProvider = new Mock<IMongoDbClientProvider>(MockBehavior.Strict);
        mongoDbClientProvider.Setup(x => x.GetClient(It.IsAny<MongoUrl>())).Returns((MongoUrl mongoUrl) => new MongoClient(MongoClientSettings.FromUrl(mongoUrl)));
        mocker.Use(mongoDbClientProvider);

        mocker.Use((IExecuteLimiter)new ExecuteLimiter(Mock.Of<IOptions<ExecuteLimiterOptions>>(x => x.Value == new ExecuteLimiterOptions()), null));
        mocker.Use(new Mock<ICollectionPool>(MockBehavior.Loose));
        mocker.Use<IInitiationLibrary>(new InitiationLibrary());

        _factory = mocker.CreateInstance<MongoDbServiceFactory>();
    }

    private DiskTestRepositoryCollection BuildCollection(params ICollectionInterceptor[] interceptors)
    {
        _factory.Interceptors = interceptors;
        return new DiskTestRepositoryCollection(_factory, _databaseContext);
    }

    private static long MeasureAllocations(Action action)
    {
        for (var i = 0; i < WarmupIterations; i++) action();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < MeasuredIterations; i++) action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void MeasurementHarness_DetectsAllocation()
    {
        // Guards every "allocates nothing" assertion below from passing for the wrong reason. If the
        // harness ever stopped measuring, they would all go green while the path regressed.
        var allocated = MeasureAllocations(() => _ = new object());

        allocated.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RegisteredInterceptor_DoesAllocate_SoZeroIsMeaningful()
    {
        // The contrast case: with an interceptor that matches the point, a CollectionCallInfo really
        // is built. Together with the zero-assertions this shows the flag is what makes the
        // difference, not the measurement being blind to this code path.
        var sut = BuildCollection(new InertInterceptor());

        var allocated = MeasureAllocations(() => sut.RunInvocationInterceptorsAsync("GetOneAsync", Operation.Read, CancellationToken.None));

        allocated.Should().BeGreaterThan(0);
    }

    [Fact]
    public void NoInterceptors_InvocationPath_AllocatesNothing()
    {
        var sut = BuildCollection();

        var allocated = MeasureAllocations(() => sut.RunInvocationInterceptorsAsync("GetOneAsync", Operation.Read, CancellationToken.None));

        allocated.Should().Be(0);
    }

    [Fact]
    public void NoInterceptors_EnumerationPath_AllocatesNothing()
    {
        var sut = BuildCollection();

        var allocated = MeasureAllocations(() => sut.RunEnumerationInterceptorsAsync("GetAsync", CancellationToken.None));

        allocated.Should().Be(0);
    }

    [Fact]
    public void NoInterceptors_StreamingEntryPath_AllocatesNothing()
    {
        // BeginInvocationInterception returns ValueTask? — a struct, so the null-vs-pending signal
        // costs nothing. This is the path every GetAsync call takes.
        var sut = BuildCollection();

        var allocated = MeasureAllocations(() => sut.BeginInvocationInterception("GetAsync", Operation.Read, CancellationToken.None));

        allocated.Should().Be(0);
    }

    [Fact]
    public void EnumerationOnlyInterceptor_LeavesTheInvocationPathFree()
    {
        // A registered interceptor that did not ask for this point must not cost anything at it —
        // this is what the two separate precomputed flags buy, rather than one "any interceptor".
        var sut = BuildCollection(new InertInterceptor { DeclaredPoints = InterceptionPoint.Enumeration });

        var allocated = MeasureAllocations(() => sut.RunInvocationInterceptorsAsync("GetOneAsync", Operation.Read, CancellationToken.None));

        allocated.Should().Be(0);
    }

    [Fact]
    public void InvocationOnlyInterceptor_LeavesTheEnumerationPathFree()
    {
        var sut = BuildCollection(new InertInterceptor { DeclaredPoints = InterceptionPoint.Invocation });

        var allocated = MeasureAllocations(() => sut.RunEnumerationInterceptorsAsync("GetAsync", CancellationToken.None));

        allocated.Should().Be(0);
    }

    [Fact]
    public void RegisteredInterceptor_BuildsCallInfoOncePerCall_NotPerInterceptor()
    {
        // The slow path does allocate — that is expected and paid for. What must hold is that the
        // CollectionCallInfo is built once and shared across the chain, not rebuilt per interceptor.
        var first = new InertInterceptor();
        var second = new InertInterceptor();
        var sut = BuildCollection(first, second);

        sut.RunInvocationInterceptorsAsync("GetOneAsync", Operation.Read, CancellationToken.None);

        first.LastCall.Should().NotBeNull();
        second.LastCall.Should().BeSameAs(first.LastCall);
    }

    private class InertInterceptor : ICollectionInterceptor
    {
        public InterceptionPoint DeclaredPoints { get; init; } = InterceptionPoint.Invocation;
        public InterceptionPoint Points => DeclaredPoints;
        public CollectionCallInfo LastCall { get; private set; }

        public ValueTask<InterceptDecision> BeforeCallAsync(CollectionCallInfo call, CancellationToken cancellationToken = default)
        {
            LastCall = call;
            return ValueTask.FromResult(InterceptDecision.Proceed);
        }
    }
}
