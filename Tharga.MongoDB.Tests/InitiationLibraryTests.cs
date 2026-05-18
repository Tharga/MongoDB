using System.Linq;
using FluentAssertions;
using Tharga.MongoDB;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Tests for the shape changes that drive index-failure-telemetry — see
/// `Requests.md` (2026-05-13). The library is the single source of truth that
/// `LogIndexOperationFailure` in `DiskRepositoryCollectionBase` uses to decide
/// whether to log at Error (first occurrence per process) or Warning (retry).
/// </summary>
public class InitiationLibraryTests
{
    private const string Server = "test-server";
    private const string Database = "test-db";
    private const string Collection = "test-collection";

    private static InitiationLibrary CreateInitiated()
    {
        var library = new InitiationLibrary();
        library.ShouldInitiate(Server, Database, Collection).Should().BeTrue("first call seeds the state");
        return library;
    }

    [Fact]
    public void GetFailedIndices_ReturnsEmpty_WhenNoFailuresRecorded()
    {
        var library = CreateInitiated();

        library.GetFailedIndices(Server, Database, Collection).Should().BeEmpty();
    }

    [Fact]
    public void GetFailedIndices_ReturnsEmpty_WhenCollectionNotInitiated()
    {
        // Public API surface must not throw for collections that never reached
        // ShouldInitiate — consumers calling GetFailedIndices on a quiet collection
        // shouldn't have to dance around an exception.
        var library = new InitiationLibrary();

        library.GetFailedIndices(Server, Database, Collection).Should().BeEmpty();
    }

    [Fact]
    public void AddFailedInitiateIndex_RecordsFailure()
    {
        var library = CreateInitiated();

        library.AddFailedInitiateIndex(Server, Database, Collection, IndexFailOperation.Create, "ix_unique", "E11000 duplicate key");

        var failures = library.GetFailedIndices(Server, Database, Collection);
        failures.Should().HaveCount(1);
        failures[0].Operation.Should().Be(IndexFailOperation.Create);
        failures[0].Name.Should().Be("ix_unique");
        failures[0].LastErrorMessage.Should().Be("E11000 duplicate key");
    }

    [Fact]
    public void AddFailedInitiateIndex_IsIdempotent_ForSameOperationAndName()
    {
        // Drives the latent dedup bug fix — the old List<...> would append a duplicate
        // on every repeated failure. ConcurrentDictionary keys give us idempotency.
        var library = CreateInitiated();

        library.AddFailedInitiateIndex(Server, Database, Collection, IndexFailOperation.Create, "ix_unique", "first failure");
        library.AddFailedInitiateIndex(Server, Database, Collection, IndexFailOperation.Create, "ix_unique", "first failure");
        library.AddFailedInitiateIndex(Server, Database, Collection, IndexFailOperation.Create, "ix_unique", "first failure");

        library.GetFailedIndices(Server, Database, Collection).Should().HaveCount(1);
    }

    [Fact]
    public void AddFailedInitiateIndex_OverwritesLastErrorMessage_OnRepeat()
    {
        // Same (op, name) hitting twice should retain the latest message so the
        // GetFailedIndices consumer sees the most recent failure reason.
        var library = CreateInitiated();

        library.AddFailedInitiateIndex(Server, Database, Collection, IndexFailOperation.Create, "ix_unique", "first error");
        library.AddFailedInitiateIndex(Server, Database, Collection, IndexFailOperation.Create, "ix_unique", "second error");

        library.GetFailedIndices(Server, Database, Collection).Single().LastErrorMessage.Should().Be("second error");
    }

    [Fact]
    public void AddFailedInitiateIndex_KeepsCreateAndDropAsSeparateEntries_ForSameName()
    {
        // (Create, "ix") and (Drop, "ix") are distinct failures; both should be retained.
        var library = CreateInitiated();

        library.AddFailedInitiateIndex(Server, Database, Collection, IndexFailOperation.Create, "ix", "create failed");
        library.AddFailedInitiateIndex(Server, Database, Collection, IndexFailOperation.Drop, "ix", "drop failed");

        var failures = library.GetFailedIndices(Server, Database, Collection);
        failures.Should().HaveCount(2);
        failures.Should().Contain(f => f.Operation == IndexFailOperation.Create && f.Name == "ix" && f.LastErrorMessage == "create failed");
        failures.Should().Contain(f => f.Operation == IndexFailOperation.Drop && f.Name == "ix" && f.LastErrorMessage == "drop failed");
    }

    [Fact]
    public void RecheckInitiateIndex_AllowsRetry_WhenFailuresExist()
    {
        var library = CreateInitiated();
        library.ShouldInitiateIndex(Server, Database, Collection).Should().BeTrue("first index-assure call should proceed");
        library.ShouldInitiateIndex(Server, Database, Collection).Should().BeFalse("second call should be short-circuited until something changes");

        library.AddFailedInitiateIndex(Server, Database, Collection, IndexFailOperation.Create, "ix", "boom");

        library.RecheckInitiateIndex(Server, Database, Collection).Should().BeTrue();
        library.ShouldInitiateIndex(Server, Database, Collection).Should().BeTrue("after a recheck, the next index-assure should proceed again");
    }

    [Fact]
    public void RecheckInitiateIndex_ReturnsFalse_WhenNoFailures()
    {
        var library = CreateInitiated();

        library.RecheckInitiateIndex(Server, Database, Collection).Should().BeFalse();
    }
}
