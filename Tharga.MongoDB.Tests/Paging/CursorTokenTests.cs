using System;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Paging;
using Xunit;

namespace Tharga.MongoDB.Tests.Paging;

public class CursorTokenTests
{
    // ---------- ToString / Parse round-trip ----------

    [Fact]
    public void Empty_RoundTrip()
    {
        var t = CursorToken.Empty;
        t.IsEmpty.Should().BeTrue();
        t.ToString().Should().Be(string.Empty);

        var parsed = CursorToken.Parse(t.ToString());
        parsed.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_ObjectIdSortValue()
    {
        var sortVal = ObjectId.GenerateNewId();
        var id = ObjectId.GenerateNewId();
        var token = new CursorToken("createdAt", true, sortVal, id);

        var parsed = CursorToken.Parse(token.ToString());

        parsed.Should().Be(token);
        parsed.SortFieldPath.Should().Be("createdAt");
        parsed.Ascending.Should().BeTrue();
        parsed.SortValue.AsObjectId.Should().Be(sortVal);
        parsed.Id.AsObjectId.Should().Be(id);
    }

    [Fact]
    public void RoundTrip_DateTimeSortValue()
    {
        var when = new DateTime(2026, 4, 15, 10, 30, 45, DateTimeKind.Utc);
        var token = new CursorToken("createdAt", false, when, ObjectId.GenerateNewId());

        var parsed = CursorToken.Parse(token.ToString());

        parsed.Should().Be(token);
        parsed.SortFieldPath.Should().Be("createdAt");
        parsed.Ascending.Should().BeFalse();
        parsed.SortValue.ToUniversalTime().Should().Be(when);
    }

    [Fact]
    public void RoundTrip_GuidSortValue()
    {
        var g = Guid.NewGuid();
        var token = new CursorToken("externalId", true, new BsonBinaryData(g, GuidRepresentation.Standard), ObjectId.GenerateNewId());

        var parsed = CursorToken.Parse(token.ToString());

        parsed.Should().Be(token);
        parsed.SortValue.AsBsonBinaryData.ToGuid(GuidRepresentation.Standard).Should().Be(g);
    }

    [Fact]
    public void RoundTrip_StringSortValue()
    {
        var token = new CursorToken("name", true, "Alice", ObjectId.GenerateNewId());

        var parsed = CursorToken.Parse(token.ToString());

        parsed.Should().Be(token);
        parsed.SortValue.AsString.Should().Be("Alice");
    }

    [Fact]
    public void RoundTrip_IntSortValue()
    {
        var token = new CursorToken("count", true, 42, ObjectId.GenerateNewId());

        var parsed = CursorToken.Parse(token.ToString());

        parsed.Should().Be(token);
        parsed.SortValue.AsInt32.Should().Be(42);
    }

    [Fact]
    public void RoundTrip_LongSortValue()
    {
        long big = 1_234_567_890_123L;
        var token = new CursorToken("ticks", false, big, ObjectId.GenerateNewId());

        var parsed = CursorToken.Parse(token.ToString());

        parsed.Should().Be(token);
        parsed.SortValue.AsInt64.Should().Be(big);
    }

    [Fact]
    public void RoundTrip_DecimalSortValue()
    {
        var d = 12345.6789m;
        var token = new CursorToken("price", true, d, ObjectId.GenerateNewId());

        var parsed = CursorToken.Parse(token.ToString());

        parsed.Should().Be(token);
        parsed.SortValue.AsDecimal.Should().Be(d);
    }

    [Fact]
    public void RoundTrip_NullSortValue()
    {
        var token = new CursorToken("optional", true, BsonNull.Value, ObjectId.GenerateNewId());

        var parsed = CursorToken.Parse(token.ToString());

        parsed.Should().Be(token);
        parsed.SortValue.IsBsonNull.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_DescendingDirection()
    {
        var token = new CursorToken("name", false, "Z", ObjectId.GenerateNewId());

        var parsed = CursorToken.Parse(token.ToString());

        parsed.Ascending.Should().BeFalse();
    }

    // ---------- Malformed input ----------

    [Fact]
    public void Parse_InvalidBase64Url_Throws()
    {
        Action act = () => CursorToken.Parse("not!valid$base64");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_NotABsonDocument_Throws()
    {
        var garbage = Convert.ToBase64String(new byte[] { 0xFF, 0xFE, 0xFD })
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Action act = () => CursorToken.Parse(garbage);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_BsonDocumentMissingFields_Throws()
    {
        // Build a doc that's missing the 'i' field
        var doc = new BsonDocument
        {
            ["f"] = "name",
            ["d"] = 1,
            ["v"] = "x",
            // no "i"
        };
        var bytes = doc.ToBson();
        var b64 = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Action act = () => CursorToken.Parse(b64);

        act.Should().Throw<FormatException>().WithMessage("*missing*");
    }

    [Fact]
    public void Parse_InvalidDirection_Throws()
    {
        var doc = new BsonDocument
        {
            ["f"] = "name",
            ["d"] = 99,    // not 1 or -1
            ["v"] = "x",
            ["i"] = ObjectId.GenerateNewId(),
        };
        var bytes = doc.ToBson();
        var b64 = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Action act = () => CursorToken.Parse(b64);

        act.Should().Throw<FormatException>().WithMessage("*direction must be 1 or -1*");
    }

    // ---------- ValidateForSort ----------

    [Fact]
    public void ValidateForSort_DifferentField_Throws()
    {
        var token = new CursorToken("name", true, "Alice", ObjectId.GenerateNewId());

        Action act = () => token.ValidateForSort("createdAt", true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not transferable across sorts*");
    }

    [Fact]
    public void ValidateForSort_DifferentDirection_Throws()
    {
        var token = new CursorToken("name", true, "Alice", ObjectId.GenerateNewId());

        Action act = () => token.ValidateForSort("name", false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not transferable across sorts*");
    }

    [Fact]
    public void ValidateForSort_SameSort_DoesNotThrow()
    {
        var token = new CursorToken("name", true, "Alice", ObjectId.GenerateNewId());

        Action act = () => token.ValidateForSort("name", true);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateForSort_EmptyToken_DoesNotThrow()
    {
        Action act = () => CursorToken.Empty.ValidateForSort("anything", true);

        act.Should().NotThrow();
    }

    // ---------- From(entity, sortBy, ascending) ----------

    [Fact]
    public void From_NullSortBy_BuildsIdOnlyCursor()
    {
        var entity = new TestEntity { Id = ObjectId.GenerateNewId(), Name = "Alice", Count = 5 };

        var token = CursorToken.From<TestEntity, ObjectId>(entity, sortBy: null, ascending: true);

        token.SortFieldPath.Should().Be("_id");
        token.SortValue.AsObjectId.Should().Be(entity.Id);
        token.Id.AsObjectId.Should().Be(entity.Id);
        token.Ascending.Should().BeTrue();
    }

    [Fact]
    public void From_StringSortField_RoundTripsValueAndPath()
    {
        var entity = new TestEntity { Id = ObjectId.GenerateNewId(), Name = "Alice", Count = 5 };

        var token = CursorToken.From<TestEntity, ObjectId>(entity, e => e.Name, ascending: true);

        token.SortFieldPath.Should().Be("Name");
        token.SortValue.AsString.Should().Be("Alice");
        token.Id.AsObjectId.Should().Be(entity.Id);
    }

    [Fact]
    public void From_IntSortField_RoundTripsValue()
    {
        var entity = new TestEntity { Id = ObjectId.GenerateNewId(), Name = "x", Count = 7 };

        var token = CursorToken.From<TestEntity, ObjectId>(entity, e => e.Count, ascending: false);

        token.SortFieldPath.Should().Be("Count");
        // boxed via Expression<Func<T, object>> — comes back as int
        token.SortValue.AsInt32.Should().Be(7);
        token.Ascending.Should().BeFalse();
    }

    [Fact]
    public void From_NullEntity_Throws()
    {
        Action act = () => CursorToken.From<TestEntity, ObjectId>(null, e => e.Name, true);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void From_RoundTripsThroughString()
    {
        var entity = new TestEntity { Id = ObjectId.GenerateNewId(), Name = "Alice", Count = 5 };

        var original = CursorToken.From<TestEntity, ObjectId>(entity, e => e.Name, true);
        var parsed = CursorToken.Parse(original.ToString());

        parsed.Should().Be(original);
        parsed.SortFieldPath.Should().Be("Name");
        parsed.SortValue.AsString.Should().Be("Alice");
        parsed.Id.AsObjectId.Should().Be(entity.Id);
    }

    private sealed record TestEntity : EntityBase<ObjectId>
    {
        public string Name { get; init; }
        public int Count { get; init; }
    }
}
