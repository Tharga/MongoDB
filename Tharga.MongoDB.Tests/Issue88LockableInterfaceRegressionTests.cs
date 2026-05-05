using System;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Tharga.MongoDB.Lockable;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Regression coverage for GitHub issue #88. The auto-registration scan in
/// <c>AddMongoDB</c> counts all non-generic interfaces a collection class implements,
/// excluding <c>IReadOnlyRepositoryCollection</c> and <c>IRepositoryCollection</c> by hand.
/// When transactions added <c>IDocumentLeaseTransactionRunner</c> to
/// <c>LockableRepositoryCollectionBase</c>, any consumer subclass that also declared its
/// own custom repository interface ended up with two surviving entries and tripped the
/// "more than one interface" validation. The fix is to exclude every interface in a
/// <c>Tharga.MongoDB</c> namespace, not just the two named.
/// </summary>
public class Issue88LockableInterfaceRegressionTests
{
    [Fact]
    public void AddMongoDB_LockableSubclassWithCustomInterface_RegistersWithoutThrowing()
    {
        var services = new ServiceCollection().AddLogging();
        var configuration = new ConfigurationBuilder().Build();

        Action act = () => services.AddMongoDB(configuration, o =>
        {
            o.AutoRegisterRepositories = false;
            o.AutoRegisterCollections = true;
            o.AutoRegistrationAssemblies = new[] { typeof(IntegrationCollection).Assembly };
        });

        act.Should().NotThrow();

        using var provider = services.BuildServiceProvider();
        provider.GetService<IIntegrationCollection>().Should().BeOfType<IntegrationCollection>();
    }

    // Test types — kept in the same assembly so the auto-registration scan picks them up.
    public record IntegrationMessage : LockableEntityBase<ObjectId>
    {
        public string Payload { get; init; }
    }

    public interface IIntegrationCollection : ILockableRepositoryCollection<IntegrationMessage, ObjectId>
    {
    }

    public class IntegrationCollection
        : LockableRepositoryCollectionBase<IntegrationMessage, ObjectId>, IIntegrationCollection
    {
        public IntegrationCollection(IMongoDbServiceFactory mongoDbServiceFactory)
            : base(mongoDbServiceFactory)
        {
        }

        public override string CollectionName => "issue88-integration";

        protected override bool RequireActor => false;
    }
}
