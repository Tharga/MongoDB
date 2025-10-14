using System.Collections.Generic;

namespace Tharga.MongoDB;

public interface ICollectionTypeService
{
    IEnumerable<CollectionType> GetCollectionTypes();
}