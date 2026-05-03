using System.Collections;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Verification that <see cref="Tharga.MongoDB.Lockable.LockableRepositoryCollectionBase{TEntity, TKey}.CoreIndices"/>
/// matches the shape of the lock-check filter (<c>UnlockedOrExpiredFilter</c> /
/// <c>CreateLockAsync</c> match-filter): if those drift apart, lockable consumers
/// silently lose their query coverage. This test fails at build time so drift is
/// caught before it ships.
/// </summary>
public class LockableCoreIndicesShapeTest : MongoDbTestBase
{
    [Fact]
    public void CoreIndices_CoverLockField()
    {
        var sut = new LockableTestRepositoryCollection(MongoDbServiceFactory);

        var rendered = RenderCoreIndices(sut);

        rendered.Should().ContainSingle(x => x.Name == "Lock")
            .Which.Keys.Names.Should().BeEquivalentTo(new[] { "Lock" });
    }

    [Fact]
    public void CoreIndices_CoverLockStatusFields()
    {
        // The "expired but unlocked" branch of UnlockedOrExpiredFilter requires:
        //   Lock.ExceptionInfo == null AND Lock.ExpireTime < now
        // The compound LockStatus index covers ExceptionInfo, ExpireTime, LockTime
        // — so the same index also serves diagnostic queries that filter by lock time.
        var sut = new LockableTestRepositoryCollection(MongoDbServiceFactory);

        var rendered = RenderCoreIndices(sut);

        var lockStatus = rendered.Should().ContainSingle(x => x.Name == "LockStatus").Subject;
        lockStatus.Keys.Names.Should().BeEquivalentTo(
            new[] { "Lock.ExceptionInfo", "Lock.ExpireTime", "Lock.LockTime" },
            opts => opts.WithStrictOrdering());
    }

    private static (string Name, BsonDocument Keys)[] RenderCoreIndices(LockableTestRepositoryCollection sut)
    {
        var registry = BsonSerializer.SerializerRegistry;
        var serializer = registry.GetSerializer<LockableTestEntity>();
        var args = new RenderArgs<LockableTestEntity>(serializer, registry);

        // CoreIndices is internal on the base class, and BindingFlags.NonPublic|Instance
        // doesn't find inherited non-public members on a derived type — walk up explicitly.
        var type = sut.GetType();
        PropertyInfo prop = null;
        while (type != null && prop == null)
        {
            prop = type.GetProperty("CoreIndices",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            type = type.BaseType;
        }
        prop.Should().NotBeNull("CoreIndices should be reachable via reflection on the lockable base");

        var indices = (IEnumerable)prop.GetValue(sut);
        return indices.Cast<CreateIndexModel<LockableTestEntity>>()
            .Select(x => (x.Options.Name, x.Keys.Render(args)))
            .ToArray();
    }
}
