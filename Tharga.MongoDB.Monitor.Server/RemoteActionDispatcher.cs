using System;
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
}
