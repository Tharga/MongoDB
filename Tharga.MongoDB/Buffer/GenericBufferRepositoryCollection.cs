using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Tharga.MongoDB.Buffer;

internal class GenericBufferRepositoryCollection<TEntity, TKey> : BufferRepositoryCollectionBase<TEntity, TKey>
    where TEntity : EntityBase<TKey>
{
    public GenericBufferRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<BufferRepositoryCollectionBase<TEntity, TKey>> logger, DatabaseContext databaseContext)
        : base(mongoDbServiceFactory, logger, databaseContext)
    {
    }
}