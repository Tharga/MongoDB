using System;
using FluentAssertions;
using MongoDB.Driver;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class DriverCompatibilityTests
{
    [Fact]
    public void DriverStillConnectsToCosmosDbServer42()
    {
        //Arrange
        var lastDriverWithServer42 = new Version(3, 10);

        //Act
        var driverVersion = typeof(MongoClient).Assembly.GetName().Version;

        //Assert
        driverVersion.Should().BeLessThan(lastDriverWithServer42, "MongoDB.Driver 3.10+ refuses MongoDB Server 4.2, which Azure Cosmos DB for MongoDB accounts on 4.2 report (#158)");
    }
}
