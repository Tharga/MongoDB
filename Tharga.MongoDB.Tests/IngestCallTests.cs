using System;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Tharga.MongoDB.Configuration;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class IngestCallTests
{
    private readonly CallLibrary _callLibrary;

    public IngestCallTests()
    {
        var options = Options.Create(new DatabaseOptions
        {
            Monitor = new MonitorOptions
            {
                LastCallsToKeep = 100,
                SlowCallsToKeep = 50,
            }
        });
        _callLibrary = new CallLibrary(options);
    }

    [Fact]
    public void IngestedCall_AppearsInLastCalls()
    {
        // Arrange
        var call = new CallInfo
        {
            Key = Guid.NewGuid(),
            StartTime = DateTime.UtcNow.AddSeconds(-1),
            SourceName = "RemoteAgent",
            Fingerprint = new CollectionFingerprint
            {
                ConfigurationName = "Default",
                DatabaseName = "RemoteDb",
                CollectionName = "Orders"
            },
            FunctionName = "GetAsync",
            Operation = Disk.Operation.Read,
            Elapsed = TimeSpan.FromMilliseconds(42),
            Count = 10,
            Final = true
        };

        // Act
        _callLibrary.IngestCall(call);

        // Assert
        var calls = _callLibrary.GetLastCalls().ToArray();
        calls.Should().ContainSingle(c => c.Key == call.Key);
        var found = calls.Single(c => c.Key == call.Key);
        found.SourceName.Should().Be("RemoteAgent");
        found.Fingerprint.DatabaseName.Should().Be("RemoteDb");
        found.FunctionName.Should().Be("GetAsync");
        found.Elapsed.Should().Be(TimeSpan.FromMilliseconds(42));
        found.Count.Should().Be(10);
        found.Final.Should().BeTrue();
    }

    [Fact]
    public void IngestedCall_AppearsInSlowCalls()
    {
        // Arrange
        var call = new CallInfo
        {
            Key = Guid.NewGuid(),
            StartTime = DateTime.UtcNow.AddSeconds(-5),
            SourceName = "RemoteAgent",
            Fingerprint = new CollectionFingerprint
            {
                ConfigurationName = "Default",
                DatabaseName = "RemoteDb",
                CollectionName = "Orders"
            },
            FunctionName = "GetAsync",
            Operation = Disk.Operation.Read,
            Elapsed = TimeSpan.FromMilliseconds(5000),
            Count = 1,
            Final = true
        };

        // Act
        _callLibrary.IngestCall(call);

        // Assert
        var slowCalls = _callLibrary.GetSlowCalls().ToArray();
        slowCalls.Should().ContainSingle(c => c.Key == call.Key);
    }

    [Fact]
    public void IngestedCall_IncrementsCallCount()
    {
        // Arrange
        var call = new CallInfo
        {
            Key = Guid.NewGuid(),
            StartTime = DateTime.UtcNow,
            SourceName = "RemoteAgent",
            Fingerprint = new CollectionFingerprint
            {
                ConfigurationName = "Default",
                DatabaseName = "RemoteDb",
                CollectionName = "Orders"
            },
            FunctionName = "GetAsync",
            Operation = Disk.Operation.Read,
            Final = true
        };

        // Act
        _callLibrary.IngestCall(call);

        // Assert
        var counts = _callLibrary.GetCallCounts();
        counts.Should().ContainKey(call.Fingerprint.Key);
        counts[call.Fingerprint.Key].Should().Be(1);
    }
}
