using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tharga.Communication.Server.Communication;
using Tharga.MongoDB.Monitor.Client;

namespace Tharga.MongoDB.Monitor.Server;

/// <summary>
/// Dispatches collection actions to a remote agent via Tharga.Communication.
/// </summary>
internal sealed class RemoteActionDispatcher : IRemoteActionDispatcher
{
    private readonly IServerCommunication _serverCommunication;

    public RemoteActionDispatcher(IServerCommunication serverCommunication)
    {
        _serverCommunication = serverCommunication;
    }

    public async Task TouchAsync(string connectionId, CollectionInfo collectionInfo, CancellationToken cancellationToken = default)
    {
        var response = await _serverCommunication.SendMessageAsync<TouchCollectionRequest, TouchCollectionResponse>(
            connectionId,
            new TouchCollectionRequest
            {
                ConfigurationName = collectionInfo.ConfigurationName.Value,
                DatabaseName = collectionInfo.DatabaseName,
                CollectionName = collectionInfo.CollectionName,
            });

        if (!response.IsSuccess)
            throw new InvalidOperationException($"Remote touch failed: {response.Message}");
        if (!response.Value.Success)
            throw new InvalidOperationException($"Remote touch failed: {response.Value.Error}");
    }

    public async Task<(int Before, int After)> DropIndexAsync(string connectionId, CollectionInfo collectionInfo, CancellationToken cancellationToken = default)
    {
        var response = await _serverCommunication.SendMessageAsync<DropIndexRequest, DropIndexResponse>(
            connectionId,
            new DropIndexRequest
            {
                ConfigurationName = collectionInfo.ConfigurationName.Value,
                DatabaseName = collectionInfo.DatabaseName,
                CollectionName = collectionInfo.CollectionName,
            });

        if (!response.IsSuccess)
            throw new InvalidOperationException($"Remote drop index failed: {response.Message}");
        if (!response.Value.Success)
            throw new InvalidOperationException($"Remote drop index failed: {response.Value.Error}");

        return (response.Value.Before, response.Value.After);
    }

    public async Task RestoreIndexAsync(string connectionId, CollectionInfo collectionInfo, bool force, CancellationToken cancellationToken = default)
    {
        var response = await _serverCommunication.SendMessageAsync<RestoreIndexRequest, RestoreIndexResponse>(
            connectionId,
            new RestoreIndexRequest
            {
                ConfigurationName = collectionInfo.ConfigurationName.Value,
                DatabaseName = collectionInfo.DatabaseName,
                CollectionName = collectionInfo.CollectionName,
                Force = force,
            });

        if (!response.IsSuccess)
            throw new InvalidOperationException($"Remote restore index failed: {response.Message}");
        if (!response.Value.Success)
            throw new InvalidOperationException($"Remote restore index failed: {response.Value.Error}");
    }

    public async Task<CleanInfo> CleanAsync(string connectionId, CollectionInfo collectionInfo, bool cleanGuids, CancellationToken cancellationToken = default)
    {
        var response = await _serverCommunication.SendMessageAsync<CleanCollectionRequest, CleanCollectionResponse>(
            connectionId,
            new CleanCollectionRequest
            {
                ConfigurationName = collectionInfo.ConfigurationName.Value,
                DatabaseName = collectionInfo.DatabaseName,
                CollectionName = collectionInfo.CollectionName,
                CleanGuids = cleanGuids,
            });

        if (!response.IsSuccess)
            throw new InvalidOperationException($"Remote clean failed: {response.Message}");
        if (!response.Value.Success)
            throw new InvalidOperationException($"Remote clean failed: {response.Value.Error}");

        return response.Value.CleanInfo;
    }

    public async Task<IEnumerable<string[]>> GetIndexBlockersAsync(string connectionId, CollectionInfo collectionInfo, string indexName, CancellationToken cancellationToken = default)
    {
        var response = await _serverCommunication.SendMessageAsync<GetIndexBlockersRequest, GetIndexBlockersResponse>(
            connectionId,
            new GetIndexBlockersRequest
            {
                ConfigurationName = collectionInfo.ConfigurationName.Value,
                DatabaseName = collectionInfo.DatabaseName,
                CollectionName = collectionInfo.CollectionName,
                IndexName = indexName,
            });

        if (!response.IsSuccess)
            throw new InvalidOperationException($"Remote get index blockers failed: {response.Message}");
        if (!response.Value.Success)
            throw new InvalidOperationException($"Remote get index blockers failed: {response.Value.Error}");

        return response.Value.Blockers;
    }

    public async Task<string> GetExplainAsync(string connectionId, Guid callKey, CancellationToken cancellationToken = default)
    {
        var response = await _serverCommunication.SendMessageAsync<ExplainRequest, ExplainResponse>(
            connectionId,
            new ExplainRequest { CallKey = callKey });

        if (!response.IsSuccess)
            throw new InvalidOperationException($"Remote explain failed: {response.Message}");
        if (!response.Value.Success)
            throw new InvalidOperationException($"Remote explain failed: {response.Value.Error}");

        return response.Value.ExplainJson;
    }

    public async Task ResetCacheAllAsync(CancellationToken cancellationToken = default)
    {
        await _serverCommunication.PostToAllAsync(new ResetCacheRequest());
    }

    public async Task ClearCallHistoryAllAsync(CancellationToken cancellationToken = default)
    {
        await _serverCommunication.PostToAllAsync(new ClearCallHistoryRequest());
    }
}
