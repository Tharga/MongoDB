using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tharga.Communication.Client.Communication;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Monitor.Client;
using Tharga.MongoDB.Monitor.Server;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// End-to-end live-monitoring test over a real Tharga.Communication client+server.
/// The client (CommunicationOptions) exposes no HttpMessageHandlerFactory/HubConnection hook,
/// so an in-process TestServer cannot be injected — the server is hosted on a real loopback
/// Kestrel port and the client connects to it over real SignalR.
///
/// It pins down the live-queue path the production code depends on:
///   server SubscribeAsync() -> agent HasSubscribers&lt;LiveMonitoringMarker&gt;() flips true
///   -> agent queue tick posts MonitorQueueMetricMessage -> server ingests it.
/// </summary>
[Trait("Category", "Integration")]
public class LiveMonitoringIntegrationTests
{
    private const string AgentSourceName = "IntegrationTestAgent";
    private const string TestServerKey = "integration::server-1";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task LiveSubscription_FlipsAgentHasSubscribers_AndAgentForwardsQueueMetrics()
    {
        var serverMonitor = new RecordingDatabaseMonitor();

        await using var server = await StartServerAsync(serverMonitor);
        var serverUrl = GetServerUrl(server);

        using var client = await StartClientAsync(serverUrl);
        var clientCommunication = client.Services.GetRequiredService<IClientCommunication>();

        (await WaitUntilAsync(() => clientCommunication.IsConnected, Timeout))
            .Should().BeTrue("the agent must connect to the loopback monitor server");

        // Before any subscriber, the agent must not believe a live view is watching.
        clientCommunication.HasSubscribers<LiveMonitoringMarker>()
            .Should().BeFalse("no Queue view is open yet");

        var subscription = await server.Services
            .GetRequiredService<ILiveMonitoringSubscription>().SubscribeAsync();

        try
        {
            (await WaitUntilAsync(() => clientCommunication.HasSubscribers<LiveMonitoringMarker>(), Timeout))
                .Should().BeTrue("once the server has a live subscriber, the agent's " +
                                 "HasSubscribers<LiveMonitoringMarker>() must flip true — this is the gate that " +
                                 "lets the queue tick start forwarding metrics");

            (await WaitUntilAsync(() => serverMonitor.HasQueueMetricFor(AgentSourceName, TestServerKey), Timeout))
                .Should().BeTrue("with a live subscriber and a non-empty pool, the agent's queue tick must post a " +
                                 "MonitorQueueMetricMessage that the server ingests");

            var ingested = serverMonitor.LastPoolMetric(AgentSourceName, TestServerKey);
            ingested.QueueCount.Should().Be(3);
            ingested.ExecutingCount.Should().Be(1);
        }
        finally
        {
            await subscription.DisposeAsync();
        }

        (await WaitUntilAsync(() => !clientCommunication.HasSubscribers<LiveMonitoringMarker>(), Timeout))
            .Should().BeTrue("after the last subscriber is gone the agent must stop seeing a subscriber");
    }

    private static async Task<WebApplication> StartServerAsync(RecordingDatabaseMonitor monitor)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddSingleton<IDatabaseMonitor>(monitor);
        builder.AddMongoDbMonitorServer(_ => { });

        var app = builder.Build();
        app.UseMongoDbMonitorServer();
        await app.StartAsync();
        return app;
    }

    private static async Task<IHost> StartClientAsync(string serverUrl)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton<IDatabaseMonitor>(new RecordingDatabaseMonitor());
        builder.Services.AddSingleton<IMongoDbServiceFactory>(new FakeMongoDbServiceFactory(AgentSourceName));
        builder.Services.AddSingleton<IQueueMonitor>(new FakeQueueMonitor(TestServerKey));
        builder.Services.AddSingleton<IConnectionPoolMonitor>(new FakeConnectionPoolMonitor());
        builder.Services.Configure<DatabaseOptions>(o =>
        {
            o.Monitor.Enabled = true;
            o.Monitor.ForwardCompletedCalls = false;
            o.Monitor.QueueMetricInterval = TimeSpan.FromMilliseconds(100);
        });

        builder.AddMongoDbMonitorClient(sendTo: serverUrl);

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private static string GetServerUrl(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        var url = addresses?.FirstOrDefault();
        url.Should().NotBeNullOrEmpty("the loopback server must expose its bound address");
        return url!;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (condition()) return true;
            await Task.Delay(50);
        }
        return condition();
    }

    private sealed class FakeMongoDbServiceFactory : IMongoDbServiceFactory
    {
        public FakeMongoDbServiceFactory(string sourceName) => SourceName = sourceName;

        public event EventHandler<CollectionAccessEventArgs> CollectionAccessEvent { add { } remove { } }
        public event EventHandler<IndexUpdatedEventArgs> IndexUpdatedEvent { add { } remove { } }
        public event EventHandler<CollectionDroppedEventArgs> CollectionDroppedEvent { add { } remove { } }
        public event EventHandler<CallStartEventArgs> CallStartEvent { add { } remove { } }
        public event EventHandler<CallEndEventArgs> CallEndEvent { add { } remove { } }

        public string SourceName { get; }
        public bool AllowDelayedCommit => false;
        public IMongoDbService GetMongoDbService(Func<DatabaseContext> databaseContextLoader) => null;
    }

    private sealed class FakeQueueMonitor : IQueueMonitor
    {
        private readonly string _serverKey;
        public FakeQueueMonitor(string serverKey) => _serverKey = serverKey;

        public event EventHandler<QueueMetricEventArgs> QueueMetricEvent { add { } remove { } }
        public IReadOnlyList<QueueMetricEventArgs> GetRecentMetrics() => Array.Empty<QueueMetricEventArgs>();
        public (int QueueCount, int ExecutingCount, double LastWaitTimeMs) GetCurrentState() => (3, 1, 5);

        public IReadOnlyList<PoolQueueState> GetPerPoolState() =>
        [
            new PoolQueueState
            {
                ServerKey = _serverKey,
                ConfigurationNames = ["Default"],
                QueueCount = 3,
                ExecutingCount = 1,
                LastWaitTimeMs = 5,
            }
        ];

        public IReadOnlyList<InFlightCallInfo> GetInFlightCalls() => Array.Empty<InFlightCallInfo>();
    }

    private sealed class FakeConnectionPoolMonitor : IConnectionPoolMonitor
    {
        public void OnConnectionCreated(string serverKey) { }
        public void OnConnectionClosed(string serverKey) { }
        public void SetMaxPoolSize(string serverKey, int maxPoolSize) { }
        public IReadOnlyList<ConnectionPoolCount> GetSnapshot() => Array.Empty<ConnectionPoolCount>();
    }

    /// <summary>
    /// Full IDatabaseMonitor that records the calls the live-monitoring path exercises and returns
    /// safe defaults for everything else. Used on both sides: it records ingested queue metrics on the
    /// server and supplies empty collection enumerations on the client.
    /// </summary>
    private sealed class RecordingDatabaseMonitor : IDatabaseMonitor
    {
        private readonly object _lock = new();
        private readonly List<(string SourceName, IReadOnlyList<PoolMetricDto> Pools)> _poolMetrics = new();
        private readonly List<MonitorClientDto> _clients = new();

        public bool HasQueueMetricFor(string sourceName, string serverKey)
        {
            lock (_lock)
                return _poolMetrics.Any(m => m.SourceName == sourceName && m.Pools.Any(p => p.ServerKey == serverKey));
        }

        public PoolMetricDto LastPoolMetric(string sourceName, string serverKey)
        {
            lock (_lock)
                return _poolMetrics
                    .Where(m => m.SourceName == sourceName)
                    .SelectMany(m => m.Pools)
                    .Last(p => p.ServerKey == serverKey);
        }

        public void IngestQueueMetric(string sourceName, IReadOnlyList<PoolMetricDto> pools, string connectionId = null)
        {
            lock (_lock) _poolMetrics.Add((sourceName, pools));
        }

        public void IngestQueueMetric(string sourceName, int queueCount, int executingCount, double? waitTimeMs, string connectionId = null)
        {
            lock (_lock)
                _poolMetrics.Add((sourceName,
                [
                    new PoolMetricDto
                    {
                        ServerKey = "aggregate",
                        ConfigurationNames = Array.Empty<string>(),
                        QueueCount = queueCount,
                        ExecutingCount = executingCount,
                    }
                ]));
        }

        public void IngestClientConnected(MonitorClientDto client)
        {
            lock (_lock) _clients.Add(client);
        }

        public IEnumerable<MonitorClientDto> GetMonitorClients()
        {
            lock (_lock) return _clients.ToArray();
        }

        public void RecordClientCommunication(string sourceName, CommunicationDirection direction, string messageType, string summary) { }

        public void IngestClientStatus(string sourceName, MonitorClientStatus status, string connectionId = null) { }

        public void IngestClientDisconnected(string connectionId) { }

        public async IAsyncEnumerable<CollectionInfo> GetInstancesAsync(bool fullDatabaseScan = false, string filter = null)
        {
            await Task.CompletedTask;
            yield break;
        }

        // --- Unused by the live-monitoring path: safe defaults ---
        public void IngestCall(CallDto call, string connectionId = null) { }
        public event EventHandler<CollectionInfoChangedEventArgs> CollectionInfoChangedEvent { add { } remove { } }
        public event EventHandler<CollectionDroppedEventArgs> CollectionDroppedEvent { add { } remove { } }
        public event EventHandler MonitorClientsChanged { add { } remove { } }
        public IEnumerable<Configuration.ConfigurationName> GetConfigurations() => Array.Empty<Configuration.ConfigurationName>();
        public Task<CollectionInfo> GetInstanceAsync(CollectionFingerprint fingerprint) => Task.FromResult<CollectionInfo>(null);
        public Task RefreshStatsAsync(CollectionFingerprint fingerprint) => Task.CompletedTask;
        public Task TouchAsync(CollectionInfo collectionInfo) => Task.CompletedTask;
        public Task<(int Before, int After)> DropIndexAsync(CollectionInfo collectionInfo) => Task.FromResult((0, 0));
        public Task RestoreIndexAsync(CollectionInfo collectionInfo, bool force) => Task.CompletedTask;
        public Task<IndexAssureSummary> RestoreAllIndicesAsync(Func<CollectionInfo, bool> filter = null, IProgress<IndexAssureProgress> progress = null, CancellationToken cancellationToken = default) => Task.FromResult<IndexAssureSummary>(null);
        public Task<IEnumerable<string[]>> GetIndexBlockersAsync(CollectionInfo collectionInfo, string indexName) => Task.FromResult(Enumerable.Empty<string[]>());
        public Task<CleanInfo> CleanAsync(CollectionInfo collectionInfo, bool cleanGuids) => Task.FromResult<CleanInfo>(null);
        public bool CanExecuteActions(CollectionInfo collectionInfo) => false;
        public Task<DocumentDto> GetDocumentAsync(CollectionInfo collectionInfo, string idRaw, CancellationToken cancellationToken = default) => Task.FromResult<DocumentDto>(null);
        public Task<DocumentListDto> ListDocumentsAsync(CollectionInfo collectionInfo, DocumentListQuery query, CancellationToken cancellationToken = default) => Task.FromResult<DocumentListDto>(null);
        public Task<SchemaComparisonDto> CompareSchemaAsync(CollectionInfo collectionInfo, int sampleSize, CancellationToken cancellationToken = default) => Task.FromResult<SchemaComparisonDto>(null);
        public IEnumerable<CallInfo> GetCalls(CallType callType) => Array.Empty<CallInfo>();
        public void ResetCalls() { }
        public Task ResetAsync() => Task.CompletedTask;
        public IEnumerable<CallDto> GetCallDtos(CallType callType) => Array.Empty<CallDto>();
        public Task<string> GetExplainAsync(Guid callKey, CancellationToken cancellationToken = default) => Task.FromResult<string>(null);
        public IReadOnlyDictionary<string, int> GetCallCounts() => new Dictionary<string, int>();
        public IEnumerable<CallSummaryDto> GetCallSummary() => Array.Empty<CallSummaryDto>();
        public IEnumerable<ErrorSummaryDto> GetErrorSummary() => Array.Empty<ErrorSummaryDto>();
        public async IAsyncEnumerable<SlowCallWithIndexInfoDto> GetSlowCallsWithIndexInfoAsync() { await Task.CompletedTask; yield break; }
        public ConnectionPoolStateDto GetConnectionPoolState() => null;
        public MonitorClientDetail GetMonitorClientDetail(string sourceName, int recentCallLimit = 20) => null;
        public IReadOnlyList<CommunicationEvent> GetClientCommunication(string sourceName) => Array.Empty<CommunicationEvent>();
        public IReadOnlyList<CollectionInfo> GetCollectionsWithFailedIndices() => Array.Empty<CollectionInfo>();
        public Task<bool> SetClientCallForwardingAsync(string sourceName, bool enabled, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public void IngestCollectionInfo(RemoteCollectionInfoDto collectionInfo, string connectionId = null) { }
        public void IngestCollectionDropped(string sourceName, string configurationName, string databaseName, string collectionName, string connectionId = null) { }
        public IReadOnlyCollection<string> GetCollectionSources(string fingerprintKey) => Array.Empty<string>();
        public string FindConnectionIdBySource(string sourceName) => null;
        public IReadOnlyDictionary<string, int> GetSubscriptions() => new Dictionary<string, int>();
        public IReadOnlyDictionary<string, ConnectionPoolStateDto> GetPerPoolQueueState() => new Dictionary<string, ConnectionPoolStateDto>();
        public IReadOnlyList<InFlightCallInfo> GetInFlightCalls() => Array.Empty<InFlightCallInfo>();
        public IReadOnlyList<ClusterConnectionSummary> GetClusterConnectionSummary() => Array.Empty<ClusterConnectionSummary>();
    }
}
