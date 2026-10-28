using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
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

[Collection("Sequential")]
public class GetCollectionDeadlockTests : IDisposable
{
    private static readonly TimeSpan DeadlockTimeout = TimeSpan.FromSeconds(10);

    private readonly Mock<IRepositoryConfigurationInternal> _configurationMock;
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;
    private readonly DatabaseContext _databaseContext;

    public GetCollectionDeadlockTests()
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
        mongoDbClientProvider.Setup(x => x.GetClient(It.IsAny<MongoUrl>())).Returns((MongoUrl mongoUrl) => new MongoClient(MongoClientSettings.FromUrl(mongoUrl)));
        mocker.Use(mongoDbClientProvider);

        var executeLimiter = new ExecuteLimiter(Mock.Of<IOptions<ExecuteLimiterOptions>>(x => x.Value == new ExecuteLimiterOptions()), null);
        mocker.Use((IExecuteLimiter)executeLimiter);
        mocker.Use<ICollectionPool>(new CollectionPool());

        var initiationLibrary = new Mock<IInitiationLibrary>(MockBehavior.Loose);
        initiationLibrary.Setup(x => x.ShouldInitiate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        mocker.Use(initiationLibrary);

        _mongoDbServiceFactory = mocker.CreateInstance<MongoDbServiceFactory>();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task GetCollection_OnSingleThreadedSynchronizationContext_Completes()
    {
        //Arrange
        var sut = new AsyncInitCollection(_mongoDbServiceFactory, _databaseContext);
        using var context = new SingleThreadSynchronizationContext();

#pragma warning disable CS0618
        //Act
        var call = context.Run(() => sut.GetCollection());
#pragma warning restore CS0618
        var completed = await Task.WhenAny(call, Task.Delay(DeadlockTimeout)) == call;

        //Assert
        completed.Should().BeTrue("GetCollection must not need the synchronization context it is blocking to finish");
        (await call).Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task GetCollection_AfterBlockedCaller_DoesNotHangLaterCallers()
    {
        //Arrange
        var sut = new AsyncInitCollection(_mongoDbServiceFactory, _databaseContext);
        using var context = new SingleThreadSynchronizationContext();
#pragma warning disable CS0618
        var blockingCall = context.Run(() => sut.GetCollection());
#pragma warning restore CS0618

        //Act
        var laterCall = sut.CountAsync(x => true);
        var completed = await Task.WhenAny(laterCall, Task.Delay(DeadlockTimeout)) == laterCall;

        //Assert
        completed.Should().BeTrue("a later caller must not wait forever on the fetch lock of a blocked GetCollection");
        await blockingCall.WaitAsync(DeadlockTimeout);
    }

    public void Dispose()
    {
        var databaseName = _configurationMock.Object.GetDatabaseUrl().DatabaseName;
        _mongoDbServiceFactory.GetMongoDbService(() => null).DropDatabase(databaseName);
    }

    private sealed class AsyncInitCollection : DiskRepositoryCollectionBase<TestEntity, ObjectId>
    {
        public AsyncInitCollection(IMongoDbServiceFactory factory, DatabaseContext context)
            : base(factory, null, context)
        {
        }

        public override string CollectionName => "GetCollectionDeadlock";

        protected override async Task InitAsync(IMongoCollection<TestEntity> collection)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    private sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object State)> _queue = new();

        public SingleThreadSynchronizationContext()
        {
            new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var (callback, state) in _queue.GetConsumingEnumerable()) callback(state);
            }) { IsBackground = true }.Start();
        }

        public override void Post(SendOrPostCallback d, object state)
        {
            if (!_queue.IsAddingCompleted) _queue.Add((d, state));
        }

        public Task<T> Run<T>(Func<T> func)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(_ =>
            {
                try
                {
                    completion.SetResult(func());
                }
                catch (Exception e)
                {
                    completion.SetException(e);
                }
            }, null);
            return completion.Task;
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
        }
    }
}
