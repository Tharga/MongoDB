using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Monitor.Client;
using Tharga.MongoDB.Monitor.Server;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class MonitorServerPipelineTests
{
    [Fact]
    public async Task FullPipeline_HandlerReceivesMessage_CallAppearsInMonitor()
    {
        // Arrange — real CallLibrary + DatabaseMonitor wiring via IngestCall
        var options = Options.Create(new DatabaseOptions
        {
            Monitor = new MonitorOptions
            {
                LastCallsToKeep = 100,
                SlowCallsToKeep = 50,
            }
        });
        var callLibrary = new CallLibrary(options);
        var monitor = new IngestOnlyMonitor(callLibrary);
        var handler = new MonitorCallHandler(monitor);

        var callDto = new CallDto
        {
            Key = Guid.NewGuid(),
            StartTime = DateTime.UtcNow.AddSeconds(-2),
            SourceName = "RemoteAgent/OrderService",
            ConfigurationName = "Default",
            DatabaseName = "RemoteDb",
            CollectionName = "Orders",
            FunctionName = "GetAsync",
            Operation = "Read",
            ElapsedMs = 150,
            Count = 25,
            Final = true,
            Steps =
            [
                new CallStepDto { Step = "Queue", DeltaMs = 1.5 },
                new CallStepDto { Step = "FetchCollectionAsync", DeltaMs = 10, Message = "Cached." }
            ]
        };
        var message = new MonitorCallMessage { Call = callDto };

        // Act — simulate handler receiving the message
        await handler.Handle(message);

        // Assert — call appears in the library
        var calls = callLibrary.GetLastCalls().ToArray();
        calls.Should().ContainSingle(c => c.Key == callDto.Key);

        var found = calls.Single(c => c.Key == callDto.Key);
        found.SourceName.Should().Be("RemoteAgent/OrderService");
        found.Fingerprint.ConfigurationName.Value.Should().Be("Default");
        found.Fingerprint.DatabaseName.Should().Be("RemoteDb");
        found.Fingerprint.CollectionName.Should().Be("Orders");
        found.FunctionName.Should().Be("GetAsync");
        found.Operation.Should().Be(Disk.Operation.Read);
        found.Elapsed.Should().Be(TimeSpan.FromMilliseconds(150));
        found.Count.Should().Be(25);
        found.Final.Should().BeTrue();
        found.Steps.Should().HaveCount(2);
        found.Steps[0].Step.Should().Be("Queue");
        found.Steps[1].Message.Should().Be("Cached.");

        // Also appears in slow calls
        var slowCalls = callLibrary.GetSlowCalls().ToArray();
        slowCalls.Should().ContainSingle(c => c.Key == callDto.Key);

        // Call count incremented
        var counts = callLibrary.GetCallCounts();
        counts.Should().ContainKey(found.Fingerprint.Key);
        counts[found.Fingerprint.Key].Should().Be(1);
    }

    [Fact]
    public async Task MultipleSources_BothAppearInMonitor()
    {
        // Arrange
        var options = Options.Create(new DatabaseOptions
        {
            Monitor = new MonitorOptions
            {
                LastCallsToKeep = 100,
                SlowCallsToKeep = 50,
            }
        });
        var callLibrary = new CallLibrary(options);
        var monitor = new IngestOnlyMonitor(callLibrary);
        var handler = new MonitorCallHandler(monitor);

        var call1 = new MonitorCallMessage
        {
            Call = new CallDto
            {
                Key = Guid.NewGuid(),
                StartTime = DateTime.UtcNow,
                SourceName = "Agent-A/OrderService",
                ConfigurationName = "Default",
                DatabaseName = "DbA",
                CollectionName = "Orders",
                FunctionName = "GetAsync",
                Operation = "Read",
                ElapsedMs = 10,
                Count = 1,
                Final = true
            }
        };

        var call2 = new MonitorCallMessage
        {
            Call = new CallDto
            {
                Key = Guid.NewGuid(),
                StartTime = DateTime.UtcNow,
                SourceName = "Agent-B/PaymentService",
                ConfigurationName = "Default",
                DatabaseName = "DbB",
                CollectionName = "Payments",
                FunctionName = "AddAsync",
                Operation = "Create",
                ElapsedMs = 20,
                Count = 1,
                Final = true
            }
        };

        // Act
        await handler.Handle(call1);
        await handler.Handle(call2);

        // Assert
        var calls = callLibrary.GetLastCalls().ToArray();
        calls.Should().HaveCount(2);
        calls.Select(c => c.SourceName).Should().Contain("Agent-A/OrderService");
        calls.Select(c => c.SourceName).Should().Contain("Agent-B/PaymentService");
    }

    /// <summary>
    /// Minimal IDatabaseMonitor that only supports IngestCall, backed by a real CallLibrary.
    /// This avoids needing the full DatabaseMonitor with all its dependencies.
    /// </summary>
    private sealed class IngestOnlyMonitor : IDatabaseMonitor
    {
        private readonly CallLibrary _callLibrary;

        public IngestOnlyMonitor(CallLibrary callLibrary) => _callLibrary = callLibrary;

        public void IngestCall(CallDto call)
        {
            Enum.TryParse<Disk.Operation>(call.Operation, out var operation);
            var callInfo = new CallInfo
            {
                Key = call.Key,
                StartTime = call.StartTime,
                SourceName = call.SourceName,
                Fingerprint = new CollectionFingerprint
                {
                    ConfigurationName = call.ConfigurationName,
                    DatabaseName = call.DatabaseName,
                    CollectionName = call.CollectionName,
                },
                FunctionName = call.FunctionName,
                Operation = operation,
                Elapsed = call.ElapsedMs.HasValue ? TimeSpan.FromMilliseconds(call.ElapsedMs.Value) : null,
                Count = call.Count,
                Final = call.Final,
                Steps = call.Steps?.Select(s => new CallStepInfo
                {
                    Step = s.Step,
                    Delta = TimeSpan.FromMilliseconds(s.DeltaMs),
                    Message = s.Message
                }).ToArray()
            };
            _callLibrary.IngestCall(callInfo);
        }

        // --- Not used in these tests ---
        public event EventHandler<CollectionInfoChangedEventArgs> CollectionInfoChangedEvent { add { } remove { } }
        public event EventHandler<CollectionDroppedEventArgs> CollectionDroppedEvent { add { } remove { } }
        public System.Collections.Generic.IEnumerable<Configuration.ConfigurationName> GetConfigurations() => throw new NotImplementedException();
        public Task<CollectionInfo> GetInstanceAsync(CollectionFingerprint fingerprint) => throw new NotImplementedException();
        public System.Collections.Generic.IAsyncEnumerable<CollectionInfo> GetInstancesAsync(bool fullDatabaseScan = false, string filter = null) => throw new NotImplementedException();
        public Task RefreshStatsAsync(CollectionFingerprint fingerprint) => throw new NotImplementedException();
        public Task TouchAsync(CollectionInfo collectionInfo) => throw new NotImplementedException();
        public Task<(int Before, int After)> DropIndexAsync(CollectionInfo collectionInfo) => throw new NotImplementedException();
        public Task RestoreIndexAsync(CollectionInfo collectionInfo, bool force) => throw new NotImplementedException();
        public Task<System.Collections.Generic.IEnumerable<string[]>> GetIndexBlockersAsync(CollectionInfo collectionInfo, string indexName) => throw new NotImplementedException();
        public Task<CleanInfo> CleanAsync(CollectionInfo collectionInfo, bool cleanGuids) => throw new NotImplementedException();
        public System.Collections.Generic.IEnumerable<CallInfo> GetCalls(CallType callType) => throw new NotImplementedException();
        public void ResetCalls() => throw new NotImplementedException();
        public Task ResetAsync() => throw new NotImplementedException();
        public System.Collections.Generic.IEnumerable<CallDto> GetCallDtos(CallType callType) => throw new NotImplementedException();
        public Task<string> GetExplainAsync(Guid callKey, System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public System.Collections.Generic.IReadOnlyDictionary<string, int> GetCallCounts() => throw new NotImplementedException();
        public System.Collections.Generic.IEnumerable<CallSummaryDto> GetCallSummary() => throw new NotImplementedException();
        public System.Collections.Generic.IEnumerable<ErrorSummaryDto> GetErrorSummary() => throw new NotImplementedException();
        public System.Collections.Generic.IAsyncEnumerable<SlowCallWithIndexInfoDto> GetSlowCallsWithIndexInfoAsync() => throw new NotImplementedException();
        public ConnectionPoolStateDto GetConnectionPoolState() => throw new NotImplementedException();
        public event EventHandler MonitorClientsChanged { add { } remove { } }
        public System.Collections.Generic.IEnumerable<MonitorClientDto> GetMonitorClients() => throw new NotImplementedException();
        public void IngestClientConnected(MonitorClientDto client) => throw new NotImplementedException();
        public void IngestClientDisconnected(string connectionId) => throw new NotImplementedException();
        public void IngestCollectionInfo(RemoteCollectionInfoDto collectionInfo) => throw new NotImplementedException();
        public System.Collections.Generic.IReadOnlyCollection<string> GetCollectionSources(string fingerprintKey) => throw new NotImplementedException();
    }
}
