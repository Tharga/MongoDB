using System;
using HostSample.Entities;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;
using Microsoft.AspNetCore.DataProtection.KeyManagement;

namespace HostSample.Features.MultiTypeDiskRepo;

public interface IMyMultiTypeDiskRepoCollection : IRepositoryCollection<MyEntityBase, ObjectId>
{
}