using System.Threading;
using System.Threading.Tasks;
using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Atlas;

internal interface IAtlasTokenService
{
    Task<string> GetAccessTokenAsync(MongoDbApiAccess access, CancellationToken cancellationToken = default);
}
