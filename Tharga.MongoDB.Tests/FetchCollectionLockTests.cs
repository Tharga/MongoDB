using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Moq.AutoMock;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Internals;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Tests that FetchCollectionAsync uses a per-collection semaphore rather than
/// a single global semaphore, so different collections can initialize concurrently.
/// </summary>
[Collection("Sequential")]
public class FetchCollectionLockTests : IDisposable
{
    private readonly Mock<IRepositoryConfigurationInternal> _configurationMock;
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private readonly DatabaseContext _databaseContext;

    public FetchCollectionLockTests()
    {
        var mocker = new AutoMocker(MockBehavior.Strict);

        _databaseContext = Mock.Of<DatabaseContext>(x => x.DatabasePart == Guid.NewGuid().ToString() && x.ConfigurationName == "Default");
        _configurationMock = new Mock<IRepositoryConfigurationInternal>(MockBehavior.Strict);
        _configurationMock.Setup(x => x.GetDatabaseUrl()).Returns(() => new MongoUrl($"mongodb://localhost:27017/Tharga_MongoDb_Test_{_databaseContext.DatabasePart}"));
        _configurationMock.Setup(x => x.GetConfiguration()).Returns(Mock.Of<MongoDbConfig>(x => x.FetchSize == 100));
        _configurationMock.Setup(x => x.GetAssureIndexMode()).Returns(AssureIndexMode.ByName);
        _configurationMock.Setup(x => x.GetConfigurationName()).Returns("Default");
        _configurationMock.Setup(x => x.GetDatabaseContext()).Returns(Mock.Of<DatabaseContext>());

        var configurationLoaderMock = new Mock<IRepositoryConfigurationLoader>(MockBehavior.Strict);
        configurationLoaderMock.Setup(x => x.GetConfiguration(It.IsAny<Func<DatabaseContext>>())).Returns(_configurationMock.Object);
        mocker.Use(configurationLoaderMock);

        var mongoDbClientProvider = new Mock<IMongoDbClientProvider>(MockBehavior.Strict);
        mongoDbClientProvider.Setup(x => x.GetClient(It.IsAny<MongoUrl>())).Returns((MongoUrl mongoUrl) =>
        {
            var settings = MongoClientSettings.FromUrl(mongoUrl);
            return new MongoClient(settings);
        });
        mocker.Use(mongoDbClientProvider);

        var executeLimiter = new ExecuteLimiter(Mock.Of<IOptions<ExecuteLimiterOptions>>(x => x.Value == new ExecuteLimiterOptions { MaxConcurrent = 20 }), null);
        mocker.Use((IExecuteLimiter)executeLimiter);

        // Use a real CollectionPool so the double-check pattern works correctly
        mocker.Use<ICollectionPool>(new CollectionPool());

        // Make ShouldInitiate return true so InitAsync is actually called
        var initiationLibrary = new Mock<IInitiationLibrary>(MockBehavior.Loose);
        initiationLibrary.Setup(x => x.ShouldInitiate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        mocker.Use(initiationLibrary);

        _mongoDbServiceFactory = mocker.CreateInstance<MongoDbServiceFactory>();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task SameCollection_ConcurrentInit_InitAsyncCalledOnce()
    {
        // Arrange
        var initCount = 0;
        var collectionA1 = new GatedInitCollection(_mongoDbServiceFactory, _databaseContext, "LockTestSame",
            onInit: () => Interlocked.Increment(ref initCount));
        var collectionA2 = new GatedInitCollection(_mongoDbServiceFactory, _databaseContext, "LockTestSame",
            onInit: () => Interlocked.Increment(ref initCount));

        // Act — trigger initialization from two instances pointing at the same collection name
        await Task.WhenAll(
            collectionA1.CountAsync(x => true),
            collectionA2.CountAsync(x => true)
        );

        // Assert — InitAsync must have run exactly once due to the double-check pattern
        initCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DifferentCollections_ConcurrentInit_RunInParallel()
    {
        // Arrange
        // aStarted signals that collection A's InitAsync has begun.
        // aContinue gates collection A so it stays blocked until we release it.
        // This lets us verify that B completes independently while A is held.
        var aStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var aContinue = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var collectionA = new GatedInitCollection(_mongoDbServiceFactory, _databaseContext, "LockTestDiffA",
            onInit: () => aStarted.TrySetResult(true),
            gate: aContinue.Task);
        var collectionB = new GatedInitCollection(_mongoDbServiceFactory, _databaseContext, "LockTestDiffB");

        // Act
        var taskA = collectionA.CountAsync(x => true);
        var taskB = collectionB.CountAsync(x => true);

        // Wait until collection A is inside InitAsync (holding its per-collection semaphore)
        await aStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Collection B must complete while A is still blocked.
        // With a global lock B would be stuck waiting; with per-collection locks B proceeds freely.
        var bCompletedBeforeARelease = await Task.WhenAny(taskB, Task.Delay(TimeSpan.FromSeconds(10))) == taskB;

        // Release A and wait for both to finish
        aContinue.SetResult(true);
        await Task.WhenAll(taskA, taskB);

        // Assert
        bCompletedBeforeARelease.Should().BeTrue(
            "collection B should initialize independently without waiting for collection A");
    }

    public void Dispose()
    {
        var databaseName = _configurationMock.Object.GetDatabaseUrl().DatabaseName;
        _mongoDbServiceFactory.GetMongoDbService(() => null).DropDatabase(databaseName);
    }

    // A test collection whose InitAsync can be observed (via onInit callback) and
    // optionally held open until a gate task completes.
    private sealed class GatedInitCollection : DiskRepositoryCollectionBase<TestEntity, ObjectId>
    {
        private readonly string _collectionName;
        private readonly Action _onInit;
        private readonly Task _gate;

        public GatedInitCollection(IMongoDbServiceFactory factory, DatabaseContext context, string collectionName,
            Action onInit = null, Task gate = null)
            : base(factory, null, context)
        {
            _collectionName = collectionName;
            _onInit = onInit;
            _gate = gate;
        }

        public override string CollectionName => _collectionName;

        protected override async Task InitAsync(IMongoCollection<TestEntity> collection)
        {
            _onInit?.Invoke();
            if (_gate != null) await _gate;
        }
    }
}
