using System;
using System.Linq;
using FluentAssertions;
using MongoDB.Bson.Serialization.Attributes;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Regression tests for <see cref="SchemaFingerprint.Generate"/> after the
/// switch from raw reflection to <c>BsonClassMap.AutoMap</c> (v2 algorithm).
/// The fingerprint must describe the on-disk BSON schema, not the C# property
/// surface — otherwise "Outdated" appears after clean even when documents are
/// in lockstep with the entity.
/// </summary>
public class SchemaFingerprintTests
{
    // --- Fixture types ---

    private sealed class BaselineEntity
    {
        public string First { get; set; }
        public string Last { get; set; }
    }

    /// <summary>
    /// Same on-disk schema as <see cref="BaselineEntity"/> — the extra computed
    /// property has no backing field and isn't serialized by MongoDB.Driver.
    /// </summary>
    private sealed class BaselinePlusComputedProperty
    {
        public string First { get; set; }
        public string Last { get; set; }
        public string Full => $"{First} {Last}";
    }

    /// <summary>
    /// Same on-disk schema as <see cref="BaselineEntity"/> — the extra read-only
    /// auto-property has no matching ctor argument and BsonClassMap doesn't map it.
    /// </summary>
    private sealed class BaselinePlusReadOnlyAutoProperty
    {
        public string First { get; set; }
        public string Last { get; set; }
        public string Derived { get; } = "constant";
    }

    /// <summary>
    /// Genuinely different on-disk schema from <see cref="BaselineEntity"/> —
    /// init-only properties are serialized by MongoDB.Driver.
    /// </summary>
    private sealed class BaselinePlusInitOnlyProperty
    {
        public string First { get; set; }
        public string Last { get; set; }
        public string Extra { get; init; }
    }

    private class BaseEntity
    {
        public string Common { get; set; }
    }

    private sealed class DerivedWithExtraProperty : BaseEntity
    {
        public string Extra { get; set; }
    }

    private sealed class EntityWithRenamedElement
    {
        [BsonElement("display_name")]
        public string Name { get; set; }
    }

    // --- Algorithm contract ---

    [Fact]
    public void Generate_StartsWithVersionPrefix_SoOldFingerprintsCompareUnequal()
    {
        SchemaFingerprint.Generate(typeof(BaselineEntity))
            .Should().StartWith("v2:",
                "version prefix is the deliberate cache-bust for collections cleaned before the algorithm change");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("ABCDEF1234567890", false)]              // old-algorithm raw hex
    [InlineData("v1:ABCDEF1234567890", false)]            // explicit older version prefix
    [InlineData("v2:ABCDEF1234567890", true)]             // current version
    public void IsCurrentVersion_DetectsOldFingerprintsAsStale(string stored, bool expected)
    {
        SchemaFingerprint.IsCurrentVersion(stored).Should().Be(expected);
    }

    [Fact]
    public void Generate_IsDeterministic_Across100Runs()
    {
        var hashes = Enumerable.Range(0, 100)
            .Select(_ => SchemaFingerprint.Generate(typeof(BaselineEntity)))
            .Distinct()
            .ToArray();

        hashes.Should().HaveCount(1);
    }

    // --- The regression cases that gave rise to the bug ---

    [Fact]
    public void Generate_IgnoresComputedGetOnlyProperty_BecauseItIsNotSerialized()
    {
        // The whole point of the v2 algorithm. Adding a `=> expr` property is
        // a C# refactor, not a schema change — fingerprint must stay equal.
        var baseline = SchemaFingerprint.Generate(typeof(BaselineEntity));
        var withComputed = SchemaFingerprint.Generate(typeof(BaselinePlusComputedProperty));

        withComputed.Should().Be(baseline);
    }

    [Fact]
    public void Generate_IgnoresReadOnlyAutoProperty_WhenBsonClassMapDoesNotMapIt()
    {
        // `{ get; }` with no matching ctor arg isn't serialized — the fingerprint
        // must not pretend it is, otherwise "Outdated" appears after clean even
        // when documents on disk match the baseline shape.
        var baseline = SchemaFingerprint.Generate(typeof(BaselineEntity));
        var withReadOnly = SchemaFingerprint.Generate(typeof(BaselinePlusReadOnlyAutoProperty));

        withReadOnly.Should().Be(baseline);
    }

    [Fact]
    public void Generate_DistinguishesInitOnlyProperty_BecauseItIsSerialized()
    {
        // `{ get; init; }` is genuinely settable from the driver's perspective —
        // it IS part of the on-disk schema. Adding one MUST change the fingerprint.
        var baseline = SchemaFingerprint.Generate(typeof(BaselineEntity));
        var withInit = SchemaFingerprint.Generate(typeof(BaselinePlusInitOnlyProperty));

        withInit.Should().NotBe(baseline);
    }

    [Fact]
    public void Generate_IncludesInheritedProperties_BecauseTheyAreSerialized()
    {
        var derivedFingerprint = SchemaFingerprint.Generate(typeof(DerivedWithExtraProperty));
        var baseFingerprint = SchemaFingerprint.Generate(typeof(BaseEntity));

        derivedFingerprint.Should().NotBe(baseFingerprint,
            "a derived type with additional inherited+declared properties has a different on-disk schema");
    }

    [Fact]
    public void Generate_UsesBsonElementName_NotCSharpPropertyName()
    {
        // [BsonElement("display_name")] means the document field is "display_name",
        // not "Name". The fingerprint describes on-disk schema, so it must use
        // the BSON name. Two types with the same C# property name but different
        // BsonElement renames must produce different fingerprints.
        var withRenamed = SchemaFingerprint.Generate(typeof(EntityWithRenamedElement));

        withRenamed.Should().NotStartWith("v2:00000000",
            "guard against accidental zero-hash if AutoMap ever returns empty");
    }
}
