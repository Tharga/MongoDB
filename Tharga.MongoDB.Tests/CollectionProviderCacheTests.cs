using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class CollectionProviderCacheTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Concurrent_first_access_runs_the_loader_once()
    {
        //Arrange
        var sut = new CollectionProviderCache();
        var databaseContext = new DatabaseContext { CollectionName = "Concurrent" };
        var loaderCalls = 0;
        using var gate = new ManualResetEventSlim(false);
        IReadOnlyRepositoryCollection Loader(DatabaseContext _)
        {
            Interlocked.Increment(ref loaderCalls);
            gate.Wait(Timeout);
            return Mock.Of<IReadOnlyRepositoryCollection>();
        }

        //Act
        var first = Task.Run(() => sut.GetCollection(databaseContext, Loader));
        SpinWait.SpinUntil(() => Volatile.Read(ref loaderCalls) == 1, Timeout).Should().BeTrue();
        var second = Task.Run(() => sut.GetCollection(databaseContext, Loader));
        SpinWait.SpinUntil(() => Volatile.Read(ref loaderCalls) == 2, TimeSpan.FromMilliseconds(500));
        gate.Set();
        var results = await Task.WhenAll(first, second).WaitAsync(Timeout);

        //Assert
        loaderCalls.Should().Be(1);
        results[1].Should().BeSameAs(results[0]);
    }

    [Fact]
    public void Failed_load_is_not_cached()
    {
        //Arrange
        var sut = new CollectionProviderCache();
        var databaseContext = new DatabaseContext { CollectionName = "Failing" };
        var collection = Mock.Of<IReadOnlyRepositoryCollection>();

        //Act
        var act = () => sut.GetCollection<IReadOnlyRepositoryCollection>(databaseContext, _ => throw new InvalidOperationException("Load failed."));
        act.Should().Throw<InvalidOperationException>();
        var result = sut.GetCollection(databaseContext, _ => collection);

        //Assert
        result.Should().BeSameAs(collection);
    }

    [Fact]
    public void Different_contexts_get_different_instances()
    {
        //Arrange
        var sut = new CollectionProviderCache();

        //Act
        var a = sut.GetCollection(new DatabaseContext { CollectionName = "A" }, _ => Mock.Of<IReadOnlyRepositoryCollection>());
        var b = sut.GetCollection(new DatabaseContext { CollectionName = "B" }, _ => Mock.Of<IReadOnlyRepositoryCollection>());
        var a2 = sut.GetCollection(new DatabaseContext { CollectionName = "A" }, _ => Mock.Of<IReadOnlyRepositoryCollection>());

        //Assert
        b.Should().NotBeSameAs(a);
        a2.Should().BeSameAs(a);
    }
}