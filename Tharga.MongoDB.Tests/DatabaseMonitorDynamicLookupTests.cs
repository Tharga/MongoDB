using System;
using FluentAssertions;
using Xunit;
using Tharga.MongoDB;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Covers the name-keyed dynamic lookup that lets a collection discovered purely from a database
/// scan — never accessed in-code in this process lifetime — be classified as
/// <see cref="Registration.Dynamic"/> on first sight, as long as it uses the default collection name
/// (the common per-database/per-tenant case). Without it, such a collection is reported as
/// <see cref="Registration.NotInCode"/> with empty defined indices and its action buttons misbehave.
/// </summary>
public class DatabaseMonitorDynamicLookupTests
{
    private static DatabaseMonitor.DynColInfo MakeDynamic(string configurationName, string collectionName, string entityTypeName, Type collectionType)
    {
        return new DatabaseMonitor.DynColInfo
        {
            Discovery = Discovery.Registration,
            Type = entityTypeName,
            ConfigurationName = configurationName,
            CollectionName = collectionName,
            CollectionType = collectionType,
            DefinedIndices = [],
            EntityType = null,
        };
    }

    [Fact]
    public void BuildDynamicByNameLookup_KeysByConfigurationAndCollectionName()
    {
        var orders = MakeDynamic("Default", "OrderEntity", "OrderEntity", typeof(string));
        var invoices = MakeDynamic("Default", "InvoiceEntity", "InvoiceEntity", typeof(object));

        var lookup = DatabaseMonitor.BuildDynamicByNameLookup([orders, invoices], defaultConfigurationName: "Default");

        lookup.Should().HaveCount(2);
        lookup.Should().ContainKey(("Default", "OrderEntity"));
        lookup[("Default", "OrderEntity")].Type.Should().Be("OrderEntity");
        lookup.Should().ContainKey(("Default", "InvoiceEntity"));
    }

    [Fact]
    public void BuildDynamicByNameLookup_AppliesDefaultConfigurationName_WhenNull()
    {
        // A dynamic registration resolved without an explicit configuration name falls back to the
        // default, so a scanned collection under the default config still matches.
        var nullConfig = MakeDynamic(null, "OrderEntity", "OrderEntity", typeof(string));

        var lookup = DatabaseMonitor.BuildDynamicByNameLookup([nullConfig], defaultConfigurationName: "Default");

        lookup.Should().ContainKey(("Default", "OrderEntity"));
    }

    [Fact]
    public void BuildDynamicByNameLookup_SkipsEntriesWithoutCollectionName()
    {
        // A dynamic registration whose collection name can't be resolved at startup (e.g. it names
        // itself per-context) must not produce a bogus (config, null) key — it falls back to
        // persist-on-use instead.
        var unnamed = MakeDynamic("Default", null, "OrderEntity", typeof(string));
        var named = MakeDynamic("Default", "InvoiceEntity", "InvoiceEntity", typeof(object));

        var lookup = DatabaseMonitor.BuildDynamicByNameLookup([unnamed, named], defaultConfigurationName: "Default");

        lookup.Should().HaveCount(1);
        lookup.Should().ContainKey(("Default", "InvoiceEntity"));
    }

    [Fact]
    public void BuildDynamicByNameLookup_CollapsesDuplicateKeys()
    {
        var first = MakeDynamic("Default", "OrderEntity", "OrderEntity", typeof(string));
        var second = MakeDynamic("Default", "OrderEntity", "OrderEntity", typeof(object));

        var lookup = DatabaseMonitor.BuildDynamicByNameLookup([first, second], defaultConfigurationName: "Default");

        lookup.Should().HaveCount(1, "a duplicate (config, name) key must collapse rather than crash");
    }
}
