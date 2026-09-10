using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.Core.Servers;
using Tharga.MongoDB.Diagnostics;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests.Diagnostics;

/// <summary>
/// The dependency spans asked for by GitHub issue #157. A span has to start on <c>CommandStartedEvent</c>
/// rather than on completion, because that is when the ambient activity is still the request that made the
/// call — starting it later would produce a correctly-timed span parented to nothing.
/// </summary>
public class CommandActivityEmitterTests : IDisposable
{
    private const int WarmupIterations = 200;
    private const int MeasuredIterations = 1000;
    private const string DatabaseName = "TestDb";
    private const string CollectionName = "user";

    private static readonly DatabaseNamespace Database = new(DatabaseName);
    private static readonly ConnectionId Connection = new(new ServerId(new ClusterId(), new DnsEndPoint("db.example.com", 27017)));

    private readonly List<Activity> _captured = new();
    private ActivityListener _listener;

    private void Listen()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MongoDbDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _captured.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener?.Dispose();
    }

    private static CommandStartedEvent Started(string commandName = "find", BsonDocument command = null, int requestId = 1)
    {
        return new CommandStartedEvent(commandName, command ?? new BsonDocument { { commandName, CollectionName }, { "filter", new BsonDocument("name", "abc") } }, Database, null, requestId, Connection);
    }

    private static CommandSucceededEvent Succeeded(string commandName = "find", int requestId = 1)
    {
        return new CommandSucceededEvent(commandName, new BsonDocument("ok", 1), Database, null, requestId, Connection, TimeSpan.FromMilliseconds(12));
    }

    private static CommandFailedEvent Failed(Exception exception, string commandName = "find", int requestId = 1)
    {
        return new CommandFailedEvent(commandName, Database, exception, null, requestId, Connection, TimeSpan.FromMilliseconds(12));
    }

    private static long MeasureAllocations(Action action)
    {
        for (var i = 0; i < WarmupIterations; i++) action();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < MeasuredIterations; i++) action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void SucceededCommand_ProducesOneClientSpan()
    {
        Listen();
        var sut = new CommandActivityEmitter();

        sut.OnCommandStarted(Started());
        sut.OnCommandSucceeded(Succeeded());

        _captured.Should().ContainSingle();
        var activity = _captured.Single();
        activity.Kind.Should().Be(ActivityKind.Client);
        activity.DisplayName.Should().Be($"find {DatabaseName}.{CollectionName}");
        activity.Status.Should().Be(ActivityStatusCode.Unset);
    }

    [Fact]
    public void SucceededCommand_CarriesTheSemanticConventionTags()
    {
        Listen();
        var sut = new CommandActivityEmitter();

        sut.OnCommandStarted(Started());
        sut.OnCommandSucceeded(Succeeded());

        var tags = _captured.Single().TagObjects.ToDictionary(x => x.Key, x => x.Value);
        tags["db.system"].Should().Be("mongodb");
        tags["db.name"].Should().Be(DatabaseName);
        tags["db.operation"].Should().Be("find");
        tags["db.mongodb.collection"].Should().Be(CollectionName);
        tags["server.address"].Should().Be("db.example.com");
        tags["server.port"].Should().Be(27017);
    }

    [Fact]
    public void DatabaseLevelCommand_ReportsNoCollection()
    {
        Listen();
        var sut = new CommandActivityEmitter();

        sut.OnCommandStarted(Started("dbStats", new BsonDocument("dbStats", 1), 7));
        sut.OnCommandSucceeded(Succeeded("dbStats", 7));

        var activity = _captured.Single();
        activity.DisplayName.Should().Be($"dbStats {DatabaseName}");
        activity.TagObjects.Should().NotContain(x => x.Key == "db.mongodb.collection");
    }

    [Fact]
    public void FailedCommand_IsMarkedError()
    {
        Listen();
        var sut = new CommandActivityEmitter();

        sut.OnCommandStarted(Started());
        sut.OnCommandFailed(Failed(new TimeoutException("took too long")));

        var activity = _captured.Single();
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("took too long");
        activity.GetTagItem("error.type").Should().Be(typeof(TimeoutException).FullName);
    }

    [Fact]
    public void Span_NestsUnderTheAmbientActivity()
    {
        Listen();
        var sut = new CommandActivityEmitter();
        using var request = new Activity("request").Start();

        sut.OnCommandStarted(Started());
        sut.OnCommandSucceeded(Succeeded());

        _captured.Single().ParentId.Should().Be(request.Id);
    }

    [Fact]
    public void ConcurrentCommands_DoNotCollide()
    {
        Listen();
        var sut = new CommandActivityEmitter();

        sut.OnCommandStarted(Started("find", null, 1));
        sut.OnCommandStarted(Started("insert", new BsonDocument("insert", "audit"), 2));
        sut.OnCommandSucceeded(Succeeded("insert", 2));
        sut.OnCommandSucceeded(Succeeded("find", 1));

        _captured.Select(x => x.DisplayName).Should().BeEquivalentTo($"insert {DatabaseName}.audit", $"find {DatabaseName}.{CollectionName}");
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("isMaster")]
    [InlineData("buildInfo")]
    [InlineData("ping")]
    [InlineData("saslStart")]
    [InlineData("endSessions")]
    public void HandshakeAndHeartbeatCommands_ProduceNoSpan(string commandName)
    {
        Listen();
        var sut = new CommandActivityEmitter();

        sut.OnCommandStarted(Started(commandName, new BsonDocument(commandName, 1), 3));
        sut.OnCommandSucceeded(Succeeded(commandName, 3));

        _captured.Should().BeEmpty();
    }

    [Fact]
    public void CommandText_IsNotCapturedByDefault()
    {
        Listen();
        var sut = new CommandActivityEmitter();

        sut.OnCommandStarted(Started());
        sut.OnCommandSucceeded(Succeeded());

        _captured.Single().GetTagItem("db.statement").Should().BeNull();
    }

    [Fact]
    public void CommandText_IsCapturedWhenAskedFor()
    {
        Listen();
        var sut = new CommandActivityEmitter(captureCommandText: true);

        sut.OnCommandStarted(Started());
        sut.OnCommandSucceeded(Succeeded());

        _captured.Single().GetTagItem("db.statement").Should().BeOfType<string>().Which.Should().Contain("filter");
    }

    [Fact]
    public void MeasurementHarness_DetectsAllocation()
    {
        // Guards the zero-assertion below from passing for the wrong reason.
        var allocated = MeasureAllocations(() => _ = new object());

        allocated.Should().BeGreaterThan(0);
    }

    [Fact]
    public void WithAListener_TheEmitterDoesAllocate_SoZeroIsMeaningful()
    {
        Listen();
        var sut = new CommandActivityEmitter();
        var started = Started();

        var allocated = MeasureAllocations(() => sut.OnCommandStarted(started));

        allocated.Should().BeGreaterThan(0);
    }

    [Fact]
    public void WithNoListener_TheEmitterAllocatesNothing()
    {
        // This is what makes the option safe to default on: an application that never registers the source
        // pays a volatile read per command and nothing else.
        var sut = new CommandActivityEmitter();
        var started = Started();

        var allocated = MeasureAllocations(() => sut.OnCommandStarted(started));

        allocated.Should().Be(0);
    }
}
