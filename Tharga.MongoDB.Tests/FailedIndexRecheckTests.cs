using FluentAssertions;
using Tharga.MongoDB;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Pins the in-process behaviour of the failed-index-recheck feature:
/// <see cref="DatabaseOptions.AssureIndexAtStartup"/> and
/// <see cref="DatabaseOptions.FailedIndexRecheckInterval"/> defaults,
/// <see cref="IInitiationLibrary.ClearFailedIndex"/> semantics, and
/// <see cref="IInitiationLibrary.GetCollectionsWithFailures"/> behaviour.
/// The BackgroundService itself is exercised indirectly — the sweep code
/// is a thin loop over the same primitives covered here.
/// </summary>
public class FailedIndexRecheckTests
{
    private const string Server = "test-server";
    private const string Database = "test-db";
    private const string Collection = "test-collection";

    private static InitiationLibrary CreateInitiated(string serverName = Server, string databaseName = Database, string collectionName = Collection)
    {
        var library = new InitiationLibrary();
        library.ShouldInitiate(serverName, databaseName, collectionName).Should().BeTrue();
        return library;
    }

    [Fact]
    public void DatabaseOptions_AssureIndexAtStartup_DefaultsToFalse()
    {
        // Lazy first-access is the once-per-session default; eager startup is explicit opt-in.
        new DatabaseOptions().AssureIndexAtStartup.Should().BeFalse();
    }

    [Fact]
    public void DatabaseOptions_FailedIndexRecheckInterval_DefaultsToOneHour()
    {
        // Default-on; set to null to disable. One-hour interval is cheap (empty ticks are no-ops).
        new DatabaseOptions().FailedIndexRecheckInterval.Should().Be(System.TimeSpan.FromHours(1));
    }

    [Fact]
    public void ClearFailedIndex_RemovesOnlyMatchingEntry()
    {
        var library = CreateInitiated();
        library.AddFailedInitiateIndex(Server, Database, Collection, IndexFailOperation.Create, "ix_a", "A failed");
        library.AddFailedInitiateIndex(Server, Database, Collection, IndexFailOperation.Create, "ix_b", "B failed");
        library.AddFailedInitiateIndex(Server, Database, Collection, IndexFailOperation.Drop, "ix_a", "drop A failed");

        library.ClearFailedIndex(Server, Database, Collection, IndexFailOperation.Create, "ix_a");

        var remaining = library.GetFailedIndices(Server, Database, Collection);
        remaining.Should().HaveCount(2);
        remaining.Should().NotContain(f => f.Operation == IndexFailOperation.Create && f.Name == "ix_a");
        remaining.Should().Contain(f => f.Operation == IndexFailOperation.Create && f.Name == "ix_b");
        remaining.Should().Contain(f => f.Operation == IndexFailOperation.Drop && f.Name == "ix_a");
    }

    [Fact]
    public void ClearFailedIndex_NoOp_WhenEntryNotPresent()
    {
        var library = CreateInitiated();
        // Should not throw on a missing entry — the success path may call it speculatively.
        var act = () => library.ClearFailedIndex(Server, Database, Collection, IndexFailOperation.Create, "never-failed");
        act.Should().NotThrow();
        library.GetFailedIndices(Server, Database, Collection).Should().BeEmpty();
    }

    [Fact]
    public void ClearFailedIndex_NoOp_WhenCollectionNotInitiated()
    {
        var library = new InitiationLibrary(); // no ShouldInitiate call
        var act = () => library.ClearFailedIndex(Server, Database, Collection, IndexFailOperation.Create, "ix_a");
        act.Should().NotThrow();
    }

    [Fact]
    public void GetCollectionsWithFailures_ReturnsEmpty_WhenNothingHasFailed()
    {
        var library = CreateInitiated();
        library.GetCollectionsWithFailures().Should().BeEmpty();
    }

    [Fact]
    public void GetCollectionsWithFailures_ReturnsKey_WhenAtLeastOneFailureRecorded()
    {
        var library = CreateInitiated();
        library.AddFailedInitiateIndex(Server, Database, Collection, IndexFailOperation.Create, "ix_a", "boom");

        var failures = library.GetCollectionsWithFailures();
        failures.Should().ContainSingle();
        failures[0].ServerName.Should().Be(Server);
        failures[0].DatabaseName.Should().Be(Database);
        failures[0].CollectionName.Should().Be(Collection);
    }

    [Fact]
    public void GetCollectionsWithFailures_DropsCollection_WhenAllFailuresCleared()
    {
        // The sweep's "dormant when healthy" property depends on this: clearing the last
        // failure for a collection must remove it from the enumeration.
        var library = CreateInitiated();
        library.AddFailedInitiateIndex(Server, Database, Collection, IndexFailOperation.Create, "ix_a", "boom");
        library.GetCollectionsWithFailures().Should().ContainSingle();

        library.ClearFailedIndex(Server, Database, Collection, IndexFailOperation.Create, "ix_a");

        library.GetCollectionsWithFailures().Should().BeEmpty();
    }

    [Fact]
    public void GetCollectionsWithFailures_ReturnsMultiple_WhenSeveralCollectionsHaveFailures()
    {
        var library = CreateInitiated("server-1", "db-1", "coll-1");
        library.ShouldInitiate("server-2", "db-2", "coll-2").Should().BeTrue();
        library.ShouldInitiate("server-3", "db-3", "coll-3").Should().BeTrue();

        library.AddFailedInitiateIndex("server-1", "db-1", "coll-1", IndexFailOperation.Create, "ix", "boom");
        library.AddFailedInitiateIndex("server-3", "db-3", "coll-3", IndexFailOperation.Drop, "ix", "boom");
        // server-2 has no failure — must NOT appear.

        var failures = library.GetCollectionsWithFailures();
        failures.Should().HaveCount(2);
        failures.Should().Contain(("server-1", "db-1", "coll-1"));
        failures.Should().Contain(("server-3", "db-3", "coll-3"));
        failures.Should().NotContain(("server-2", "db-2", "coll-2"));
    }
}
