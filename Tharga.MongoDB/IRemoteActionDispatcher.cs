using System.Threading;
using System.Threading.Tasks;

namespace Tharga.MongoDB;

/// <summary>
/// Dispatches collection actions to a remote agent when the collection
/// is not accessible locally. Implemented by Monitor.Server package.
/// </summary>
public interface IRemoteActionDispatcher
{
    Task TouchAsync(string connectionId, CollectionInfo collectionInfo, CancellationToken cancellationToken = default);
    Task<(int Before, int After)> DropIndexAsync(string connectionId, CollectionInfo collectionInfo, CancellationToken cancellationToken = default);
    Task RestoreIndexAsync(string connectionId, CollectionInfo collectionInfo, bool force, CancellationToken cancellationToken = default);
    Task<CleanInfo> CleanAsync(string connectionId, CollectionInfo collectionInfo, bool cleanGuids, CancellationToken cancellationToken = default);
}
