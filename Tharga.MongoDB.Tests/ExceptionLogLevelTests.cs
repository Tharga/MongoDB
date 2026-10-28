using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Lockable;
using Tharga.MongoDB.Tests.Lockable.Base;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class ExceptionLogLevelTests : LockableTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task DuplicateKey_WithoutOverride_LogsError()
    {
        //Arrange
        var logger = new ExceptionLogger();
        var sut = new LevelCollection(_mongoDbServiceFactory, logger);
        var entity = new TestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);

        //Act
        var act = () => sut.AddAsync(entity);

        //Assert
        await act.Should().ThrowAsync<MongoWriteException>();
        logger.Levels.Should().Equal(LogLevel.Error);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DuplicateKey_OverriddenToWarning_LogsWarning()
    {
        //Arrange
        var logger = new ExceptionLogger();
        var sut = new LevelCollection(_mongoDbServiceFactory, logger, (e, _) => IsDuplicateKey(e) ? LogLevel.Warning : null);
        var entity = new TestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);

        //Act
        var act = () => sut.AddAsync(entity);

        //Assert
        await act.Should().ThrowAsync<MongoWriteException>();
        logger.Levels.Should().Equal(LogLevel.Warning);
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task DuplicateKey_OverriddenToNone_LogsNothingAndStillThrows()
    {
        //Arrange
        var logger = new ExceptionLogger();
        var sut = new LevelCollection(_mongoDbServiceFactory, logger, (e, _) => IsDuplicateKey(e) ? LogLevel.None : null);
        var entity = new TestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);

        //Act
        var act = () => sut.AddAsync(entity);

        //Assert
        await act.Should().ThrowAsync<MongoWriteException>();
        logger.Levels.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task Override_ReceivesTheOperationName()
    {
        //Arrange
        string operation = null;
        var sut = new LevelCollection(_mongoDbServiceFactory, new ExceptionLogger(), (_, op) =>
        {
            operation = op;
            return null;
        });
        var entity = new TestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);

        //Act
        var act = () => sut.AddAsync(entity);

        //Assert
        await act.Should().ThrowAsync<MongoWriteException>();
        operation.Should().Be(nameof(IRepositoryCollection<TestEntity, ObjectId>.AddAsync));
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task LockableCollection_Override_IsHonoured()
    {
        //Arrange
        var logger = new ExceptionLogger();
        var sut = new LockableLevelCollection(_mongoDbServiceFactory, logger, (e, _) => IsDuplicateKey(e) ? LogLevel.None : null);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);

        //Act
        var act = () => sut.AddAsync(entity);

        //Assert
        await act.Should().ThrowAsync<MongoWriteException>();
        logger.Levels.Should().BeEmpty();
    }

    private static bool IsDuplicateKey(Exception exception)
    {
        return exception is MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey };
    }

    private sealed class LevelCollection : DiskRepositoryCollectionBase<TestEntity, ObjectId>
    {
        private readonly Func<Exception, string, LogLevel?> _level;

        public LevelCollection(IMongoDbServiceFactory factory, ILogger logger, Func<Exception, string, LogLevel?> level = null)
            : base(factory, logger)
        {
            _level = level;
        }

        public override string CollectionName => "ExceptionLogLevel";

        protected override LogLevel GetExceptionLogLevel(Exception exception, string operation)
        {
            return _level?.Invoke(exception, operation) ?? base.GetExceptionLogLevel(exception, operation);
        }
    }

    private sealed class LockableLevelCollection : LockableRepositoryCollectionBase<LockableTestEntity, ObjectId>
    {
        private readonly Func<Exception, string, LogLevel?> _level;

        public LockableLevelCollection(IMongoDbServiceFactory factory, ILogger logger, Func<Exception, string, LogLevel?> level)
            : base(factory, logger)
        {
            _level = level;
        }

        public override string CollectionName => "LockableExceptionLogLevel";
        protected override bool RequireActor => false;

        protected override LogLevel GetExceptionLogLevel(Exception exception, string operation)
        {
            return _level?.Invoke(exception, operation) ?? base.GetExceptionLogLevel(exception, operation);
        }
    }

    private sealed class ExceptionLogger : ILogger
    {
        private readonly ConcurrentQueue<LogLevel> _levels = new();

        public LogLevel[] Levels => _levels.ToArray();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (exception != null) _levels.Enqueue(logLevel);
        }
    }
}
