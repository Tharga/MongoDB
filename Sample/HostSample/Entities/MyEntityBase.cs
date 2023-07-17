using MongoDB.Bson;
using Tharga.MongoDB;

namespace HostSample.Entities;

public abstract record MyEntityBase : EntityBase<ObjectId>;