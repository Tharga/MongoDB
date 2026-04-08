using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class TrackMongoCollectionTests
{
    [Fact]
    public void TrackMongoCollection_RegistersEntry()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.TrackMongoCollection(typeof(ITestCollection), typeof(TestCollection));

        // Assert
        var provider = services.BuildServiceProvider();
        var entries = provider.GetServices<TrackedCollectionEntry>().ToArray();
        entries.Should().ContainSingle();
        entries[0].ServiceType.Should().Be(typeof(ITestCollection));
        entries[0].ImplementationType.Should().Be(typeof(TestCollection));
    }

    [Fact]
    public void TrackMongoCollection_Generic_RegistersEntry()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.TrackMongoCollection<ITestCollection, TestCollection>();

        // Assert
        var provider = services.BuildServiceProvider();
        var entries = provider.GetServices<TrackedCollectionEntry>().ToArray();
        entries.Should().ContainSingle();
        entries[0].ServiceType.Should().Be(typeof(ITestCollection));
    }

    [Fact]
    public void TrackMongoCollection_MultipleEntries_AllRegistered()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.TrackMongoCollection(typeof(ITestCollection), typeof(TestCollection));
        services.TrackMongoCollection(typeof(ITestCollection2), typeof(TestCollection2));

        // Assert
        var provider = services.BuildServiceProvider();
        var entries = provider.GetServices<TrackedCollectionEntry>().ToArray();
        entries.Should().HaveCount(2);
    }

    // Dummy types for testing
    private interface ITestCollection { }
    private class TestCollection : ITestCollection { }
    private interface ITestCollection2 { }
    private class TestCollection2 : ITestCollection2 { }
}
