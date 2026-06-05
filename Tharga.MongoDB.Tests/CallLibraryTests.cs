using FluentAssertions;
using Microsoft.Extensions.Options;
using System;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Disk;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class CallLibraryTests
{
    private static CallLibrary NewLibrary()
    {
        return new CallLibrary(Options.Create(new DatabaseOptions { Monitor = new MonitorOptions { LastCallsToKeep = 100 } }));
    }

    private static CollectionFingerprint Fingerprint(string config, string database, string collection)
    {
        return new CollectionFingerprint
        {
            ConfigurationName = (ConfigurationName)config,
            DatabaseName = database,
            CollectionName = collection,
        };
    }

    [Fact]
    public void GetCallCountsBySuffix_SumsAcrossConfigurations()
    {
        using var sut = NewLibrary();

        // Two different ConfigurationNames pointing at the same database + collection.
        sut.StartCall(new CallStartEventArgs(Guid.NewGuid(), Fingerprint("A", "Db", "Coll"), "fn", Operation.Read));
        sut.StartCall(new CallStartEventArgs(Guid.NewGuid(), Fingerprint("B", "Db", "Coll"), "fn", Operation.Read));
        sut.StartCall(new CallStartEventArgs(Guid.NewGuid(), Fingerprint("A", "Db", "Coll"), "fn", Operation.Read));

        var bySuffix = sut.GetCallCountsBySuffix();

        bySuffix.Should().ContainKey(".Db.Coll");
        bySuffix[".Db.Coll"].Should().Be(3);
    }

    [Fact]
    public void GetCallCountsBySuffix_KeepsCollectionsSeparate()
    {
        using var sut = NewLibrary();

        sut.StartCall(new CallStartEventArgs(Guid.NewGuid(), Fingerprint("A", "Db", "One"), "fn", Operation.Read));
        sut.StartCall(new CallStartEventArgs(Guid.NewGuid(), Fingerprint("A", "Db", "Two"), "fn", Operation.Read));

        var bySuffix = sut.GetCallCountsBySuffix();

        bySuffix[".Db.One"].Should().Be(1);
        bySuffix[".Db.Two"].Should().Be(1);
    }

    [Fact]
    public void GetCallCountsBySuffix_Empty_AfterReset()
    {
        using var sut = NewLibrary();
        sut.StartCall(new CallStartEventArgs(Guid.NewGuid(), Fingerprint("A", "Db", "Coll"), "fn", Operation.Read));

        sut.ResetCalls();

        sut.GetCallCountsBySuffix().Should().BeEmpty();
    }

    [Fact]
    public void GetCallCountsBySuffix_IngestCall_AlsoUpdates()
    {
        using var sut = NewLibrary();

        sut.IngestCall(new CallInfo
        {
            Key = Guid.NewGuid(),
            Fingerprint = Fingerprint("Remote", "Db", "Coll"),
            FunctionName = "fn",
            Operation = Operation.Read,
            StartTime = DateTime.UtcNow,
            SourceName = "remote-agent",
        });

        sut.GetCallCountsBySuffix()[".Db.Coll"].Should().Be(1);
    }
}
