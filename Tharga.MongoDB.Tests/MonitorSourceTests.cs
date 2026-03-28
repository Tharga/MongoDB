using System;
using System.Reflection;
using FluentAssertions;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Disk;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class MonitorSourceTests
{
    [Fact]
    public void CallStartEventArgs_CarriesSourceName()
    {
        var fingerprint = new CollectionFingerprint
        {
            ConfigurationName = "Default",
            DatabaseName = "TestDb",
            CollectionName = "TestCol"
        };

        var args = new CallStartEventArgs(Guid.NewGuid(), fingerprint, "GetAsync", Operation.Read, "MyAgent");

        args.SourceName.Should().Be("MyAgent");
    }

    [Fact]
    public void CallStartEventArgs_SourceName_DefaultsToNull()
    {
        var fingerprint = new CollectionFingerprint
        {
            ConfigurationName = "Default",
            DatabaseName = "TestDb",
            CollectionName = "TestCol"
        };

        var args = new CallStartEventArgs(Guid.NewGuid(), fingerprint, "GetAsync", Operation.Read);

        args.SourceName.Should().BeNull();
    }

    [Fact]
    public void CallInfo_CarriesSourceName()
    {
        var info = new CallInfo
        {
            Key = Guid.NewGuid(),
            StartTime = DateTime.UtcNow,
            SourceName = "SERVER-01/OrderService",
            Fingerprint = new CollectionFingerprint
            {
                ConfigurationName = "Default",
                DatabaseName = "TestDb",
                CollectionName = "TestCol"
            },
            FunctionName = "GetAsync",
            Operation = Operation.Read
        };

        info.SourceName.Should().Be("SERVER-01/OrderService");
    }

    [Fact]
    public void CallDto_CarriesSourceName()
    {
        var dto = new CallDto
        {
            Key = Guid.NewGuid(),
            StartTime = DateTime.UtcNow,
            SourceName = "MyService",
            ConfigurationName = "Default",
            DatabaseName = "TestDb",
            CollectionName = "TestCol",
            FunctionName = "GetAsync",
            Operation = "Read"
        };

        dto.SourceName.Should().Be("MyService");
    }

    [Fact]
    public void MonitorOptions_SourceName_DefaultsToNull()
    {
        var options = new MonitorOptions();

        options.SourceName.Should().BeNull();
    }

    [Fact]
    public void MonitorOptions_SourceName_CanBeConfigured()
    {
        var options = new MonitorOptions { SourceName = "MyAgent" };

        options.SourceName.Should().Be("MyAgent");
    }
}
