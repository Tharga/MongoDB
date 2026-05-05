using System;
using System.Linq;
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
            o.AutoRegistrationAssemblies = new[] { typeof(Issue88IntegrationCollection).Assembly };
        });

        act.Should().NotThrow();

        // The bug manifests as the registration scan throwing; once the scan succeeds we only need
        // to confirm that the custom interface ended up bound to its concrete implementation, not
        // accidentally bound to the framework marker IDocumentLeaseTransactionRunner.
        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IIssue88IntegrationCollection)
            && d.ImplementationType == typeof(Issue88IntegrationCollection));
    }
}

// Test types — kept at top level so the auto-registration scan picks them up.
public record Issue88IntegrationMessage : LockableEntityBase<ObjectId>
{
    public string Payload { get; init; }
}

public interface IIssue88IntegrationCollection : ILockableRepositoryCollection<Issue88IntegrationMessage, ObjectId>
{
}

public class Issue88IntegrationCollection
    : LockableRepositoryCollectionBase<Issue88IntegrationMessage, ObjectId>, IIssue88IntegrationCollection
{
    public Issue88IntegrationCollection(IMongoDbServiceFactory mongoDbServiceFactory)
        : base(mongoDbServiceFactory)
    {
    }

    public override string CollectionName => "issue88-integration";

    protected override bool RequireActor => false;
}
