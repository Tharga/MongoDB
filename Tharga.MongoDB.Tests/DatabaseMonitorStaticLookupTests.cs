using System;
using FluentAssertions;
using Xunit;
using Tharga.MongoDB;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Pins the dedup behaviour added to `DatabaseMonitor.BuildStaticLookup` after Florida's
/// 2026-05-30 `Requests.md` entry. The crashing line was a plain `.ToDictionary(...)`
/// that threw when two `IDiskRepositoryCollection<,>` classes legitimately overlaid the
/// same physical Mongo collection as a read-projection pattern.
/// </summary>
public class DatabaseMonitorStaticLookupTests
{
    private static DatabaseMonitor.StatColInfo MakeStatic(string configurationName, string collectionName, string entityTypeName, Type collectionType)
    {
        return new DatabaseMonitor.StatColInfo
        {
            Discovery = Discovery.Registration,
            ConfigurationName = configurationName,
            CollectionName = collectionName,
            EntityTypes = [entityTypeName],
            CollectionType = collectionType,
            Registration = Registration.Static,
            DefinedIndices = [],
            EntityType = null,
        };
    }

    [Fact]
    public void BuildStaticLookup_DropsDuplicateKey_AndMergesEntityTypes()
    {
        // Florida case: TeamRepositoryCollection<TeamEntity> + TeamFortnoxReaderCollection<TeamFortnoxView>
        // both target "TeamEntity" under the default configuration.
        var writer = MakeStatic("Default", "TeamEntity", "TeamEntity", typeof(string));
        var reader = MakeStatic("Default", "TeamEntity", "TeamFortnoxView", typeof(object));

        var lookup = DatabaseMonitor.BuildStaticLookup([writer, reader], defaultConfigurationName: "Default");

        lookup.Should().HaveCount(1, "the duplicate key must collapse, not crash");
        lookup[("Default", "TeamEntity")].EntityTypes.Should().BeEquivalentTo(["TeamEntity", "TeamFortnoxView"],
            "both reader entity-type names must surface so the monitor UI shows every reader");
    }

    [Fact]
    public void BuildStaticLookup_PreservesDistinctEntries()
    {
        var a = MakeStatic("Default", "TeamEntity", "TeamEntity", typeof(string));
        var b = MakeStatic("Default", "OrderEntity", "OrderEntity", typeof(string));
        var c = MakeStatic("Other", "TeamEntity", "TeamEntity", typeof(string));

        var lookup = DatabaseMonitor.BuildStaticLookup([a, b, c], defaultConfigurationName: "Default");

        lookup.Should().HaveCount(3);
        lookup.Should().ContainKey(("Default", "TeamEntity"));
        lookup.Should().ContainKey(("Default", "OrderEntity"));
        lookup.Should().ContainKey(("Other", "TeamEntity"));
    }

    [Fact]
    public void BuildStaticLookup_AppliesDefaultConfigurationName_WhenStatColInfoHasNullConfiguration()
    {
        // GetStaticCollectionsFromCodeCore can yield StatColInfo with null ConfigurationName
        // (collections that don't explicitly set one). The lookup key falls back to
        // _options.DefaultConfigurationName so they collide with explicitly-named ones
        // under the same default.
        var explicitDefault = MakeStatic("Default", "TeamEntity", "TeamEntity", typeof(string));
        var nullConfig = MakeStatic(null, "TeamEntity", "TeamFortnoxView", typeof(object));

        var lookup = DatabaseMonitor.BuildStaticLookup([explicitDefault, nullConfig], defaultConfigurationName: "Default");

        lookup.Should().HaveCount(1);
        lookup[("Default", "TeamEntity")].EntityTypes.Should().BeEquivalentTo(["TeamEntity", "TeamFortnoxView"]);
    }
}
