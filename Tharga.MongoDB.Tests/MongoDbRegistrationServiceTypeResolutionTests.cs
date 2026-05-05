using System;
using System.Reflection;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Lockable;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Unit-level lockdown tests for the auto-registration helpers. Pinning all four cases the
/// helpers cover so a future refactor can't quietly re-introduce the regression behind #88.
/// </summary>
public class MongoDbRegistrationServiceTypeResolutionTests
{
    // ---------- IsThargaMongoDBFrameworkInterface ----------

    [Fact]
    public void IsThargaMongoDBFrameworkInterface_True_ForFrameworkInterfaces()
    {
        MongoDbRegistrationExtensions.IsThargaMongoDBFrameworkInterface(typeof(IRepositoryCollection)).Should().BeTrue();
        MongoDbRegistrationExtensions.IsThargaMongoDBFrameworkInterface(typeof(IReadOnlyRepositoryCollection)).Should().BeTrue();
        MongoDbRegistrationExtensions.IsThargaMongoDBFrameworkInterface(typeof(IDocumentLeaseTransactionRunner)).Should().BeTrue();
    }

    [Fact]
    public void IsThargaMongoDBFrameworkInterface_False_ForConsumerInterfaces()
    {
        MongoDbRegistrationExtensions.IsThargaMongoDBFrameworkInterface(typeof(IIssue88IntegrationCollection)).Should().BeFalse();
        MongoDbRegistrationExtensions.IsThargaMongoDBFrameworkInterface(typeof(IFirstResolutionCustom)).Should().BeFalse();
    }

    // ---------- ResolveCollectionServiceType ----------

    [Fact]
    public void ResolveCollectionServiceType_OneCustomInterface_ReturnsThatInterface()
    {
        var resolved = MongoDbRegistrationExtensions.ResolveCollectionServiceType(typeof(Issue88IntegrationCollection).GetTypeInfo());

        resolved.Should().Be(typeof(IIssue88IntegrationCollection));
    }

    [Fact]
    public void ResolveCollectionServiceType_NoCustomInterface_ReturnsImplementationType()
    {
        var resolved = MongoDbRegistrationExtensions.ResolveCollectionServiceType(typeof(NoCustomInterfaceLockable).GetTypeInfo());

        resolved.Should().Be(typeof(NoCustomInterfaceLockable));
    }

    [Fact]
    public void ResolveCollectionServiceType_TwoCustomInterfaces_Throws()
    {
        Action act = () => MongoDbRegistrationExtensions.ResolveCollectionServiceType(typeof(TwoCustomInterfaces).GetTypeInfo());

        act.Should().Throw<InvalidOperationException>().WithMessage("*2 interfaces*");
    }

    // ---------- Test types ----------
    // Kept separate from the auto-scan: these classes do NOT implement IReadOnlyRepositoryCollection,
    // so they are not picked up by AssemblyService.GetTypes<IReadOnlyRepositoryCollection>(...).

    private sealed record NoCustomLockableEntity : LockableEntityBase<ObjectId>;

    /// <summary>Lockable subclass with no consumer interface. Service type should be itself.</summary>
    private sealed class NoCustomInterfaceLockable : LockableRepositoryCollectionBase<NoCustomLockableEntity, ObjectId>
    {
        public NoCustomInterfaceLockable(IMongoDbServiceFactory mongoDbServiceFactory) : base(mongoDbServiceFactory) { }
        public override string CollectionName => "no-custom-interface-lockable";
        protected override bool RequireActor => false;
    }

    public interface IFirstResolutionCustom { }
    public interface ISecondResolutionCustom { }

    /// <summary>Pure dummy used only to assert the multiple-interface validation still fires.</summary>
    private sealed class TwoCustomInterfaces : IFirstResolutionCustom, ISecondResolutionCustom { }
}
