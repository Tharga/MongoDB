using System;
using System.Linq;
using System.Threading.Tasks;
using Tharga.Communication.MessageHandler;

namespace Tharga.MongoDB.Monitor.Client;

/// <summary>
/// Handles touch requests from the central server by executing locally.
/// </summary>
public sealed class TouchCollectionHandler : SendMessageHandlerBase<TouchCollectionRequest, TouchCollectionResponse>
{
    private readonly IDatabaseMonitor _monitor;

    public TouchCollectionHandler(IDatabaseMonitor monitor)
    {
        _monitor = monitor;
    }

    public override async Task<TouchCollectionResponse> Handle(TouchCollectionRequest message)
    {
        try
        {
            var instance = await FindInstanceAsync(message.ConfigurationName, message.DatabaseName, message.CollectionName);
            if (instance == null) return new TouchCollectionResponse { Error = "Collection not found locally." };

            await _monitor.TouchAsync(instance);
            return new TouchCollectionResponse { Success = true };
        }
        catch (Exception e)
        {
            return new TouchCollectionResponse { Error = e.Message };
        }
    }

    private async Task<CollectionInfo> FindInstanceAsync(string configurationName, string databaseName, string collectionName)
    {
        return await _monitor.GetInstancesAsync()
            .FirstOrDefaultAsync(x => x.ConfigurationName.Value == configurationName && x.DatabaseName == databaseName && x.CollectionName == collectionName);
    }
}

/// <summary>
/// Handles drop index requests from the central server by executing locally.
/// </summary>
public sealed class DropIndexHandler : SendMessageHandlerBase<DropIndexRequest, DropIndexResponse>
{
    private readonly IDatabaseMonitor _monitor;

    public DropIndexHandler(IDatabaseMonitor monitor)
    {
        _monitor = monitor;
    }

    public override async Task<DropIndexResponse> Handle(DropIndexRequest message)
    {
        try
        {
            var instance = await _monitor.GetInstancesAsync()
                .FirstOrDefaultAsync(x => x.ConfigurationName.Value == message.ConfigurationName && x.DatabaseName == message.DatabaseName && x.CollectionName == message.CollectionName);
            if (instance == null) return new DropIndexResponse { Error = "Collection not found locally." };

            var result = await _monitor.DropIndexAsync(instance);
            return new DropIndexResponse { Success = true, Before = result.Before, After = result.After };
        }
        catch (Exception e)
        {
            return new DropIndexResponse { Error = e.Message };
        }
    }
}

/// <summary>
/// Handles restore index requests from the central server by executing locally.
/// </summary>
public sealed class RestoreIndexHandler : SendMessageHandlerBase<RestoreIndexRequest, RestoreIndexResponse>
{
    private readonly IDatabaseMonitor _monitor;

    public RestoreIndexHandler(IDatabaseMonitor monitor)
    {
        _monitor = monitor;
    }

    public override async Task<RestoreIndexResponse> Handle(RestoreIndexRequest message)
    {
        try
        {
            var instance = await _monitor.GetInstancesAsync()
                .FirstOrDefaultAsync(x => x.ConfigurationName.Value == message.ConfigurationName && x.DatabaseName == message.DatabaseName && x.CollectionName == message.CollectionName);
            if (instance == null) return new RestoreIndexResponse { Error = "Collection not found locally." };

            await _monitor.RestoreIndexAsync(instance, message.Force);
            return new RestoreIndexResponse { Success = true };
        }
        catch (Exception e)
        {
            return new RestoreIndexResponse { Error = e.Message };
        }
    }
}

/// <summary>
/// Handles clean requests from the central server by executing locally.
/// </summary>
public sealed class CleanCollectionHandler : SendMessageHandlerBase<CleanCollectionRequest, CleanCollectionResponse>
{
    private readonly IDatabaseMonitor _monitor;

    public CleanCollectionHandler(IDatabaseMonitor monitor)
    {
        _monitor = monitor;
    }

    public override async Task<CleanCollectionResponse> Handle(CleanCollectionRequest message)
    {
        try
        {
            var instance = await _monitor.GetInstancesAsync()
                .FirstOrDefaultAsync(x => x.ConfigurationName.Value == message.ConfigurationName && x.DatabaseName == message.DatabaseName && x.CollectionName == message.CollectionName);
            if (instance == null) return new CleanCollectionResponse { Error = "Collection not found locally." };

            var result = await _monitor.CleanAsync(instance, message.CleanGuids);
            return new CleanCollectionResponse { Success = true, CleanInfo = result };
        }
        catch (Exception e)
        {
            return new CleanCollectionResponse { Error = e.Message };
        }
    }
}
