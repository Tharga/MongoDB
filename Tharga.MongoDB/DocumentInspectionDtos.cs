namespace Tharga.MongoDB;

/// <summary>
/// Single MongoDB document returned by <see cref="IDatabaseMonitor.GetDocumentAsync"/>.
/// Json is MongoDB Extended JSON — the raw shape stored in MongoDB, not a C# serializer round-trip.
/// </summary>
public record DocumentDto
{
    public required string Id { get; init; }
    public required string Json { get; init; }
}

/// <summary>
/// Input for <see cref="IDatabaseMonitor.ListDocumentsAsync"/>.
/// </summary>
public record DocumentListQuery
{
    /// <summary>Maximum documents to return. Default 20, capped at 200 by the implementation.</summary>
    public int Limit { get; init; } = 20;

    /// <summary>Documents to skip before returning results.</summary>
    public int Skip { get; init; } = 0;

    /// <summary>Optional Mongo filter as a JSON string (e.g. <c>{"Status":"Active"}</c>). Empty/null = match all.</summary>
    public string FilterJson { get; init; }

    /// <summary>Optional sort spec as a JSON string (e.g. <c>{"CreatedAt":-1}</c>). Empty/null = no sort.</summary>
    public string SortJson { get; init; }
}

/// <summary>
/// Result of <see cref="IDatabaseMonitor.ListDocumentsAsync"/>.
/// </summary>
public record DocumentListDto
{
    public required DocumentDto[] Documents { get; init; }
    public required int TotalReturned { get; init; }

    /// <summary><c>true</c> if the result was capped by Limit (more documents may exist).</summary>
    public required bool Truncated { get; init; }
}

/// <summary>
/// Result of <see cref="IDatabaseMonitor.CompareSchemaAsync"/>: a three-way diff between
/// the C# entity type's public properties, the registered collection type, and the field set
/// observed across sampled documents.
/// </summary>
public record SchemaComparisonDto
{
    /// <summary>Caller-requested sample size (capped by implementation).</summary>
    public required int SampleSize { get; init; }

    /// <summary>Actual number of documents sampled (≤ SampleSize, capped by collection size).</summary>
    public required int SampledCount { get; init; }

    /// <summary>Names of the entity types declared on the collection (from <see cref="CollectionInfo.EntityTypes"/>).</summary>
    public required string[] EntityTypes { get; init; }

    public required SchemaComparisonField[] Fields { get; init; }
}

public record SchemaComparisonField
{
    public required string Name { get; init; }
    public required SchemaFieldClassification Classification { get; init; }

    /// <summary>How many sampled docs contain this field (0..<see cref="SchemaComparisonDto.SampledCount"/>).</summary>
    public required int CoverageCount { get; init; }

    /// <summary><c>true</c> if the field corresponds to a public property on the resolved entity type.</summary>
    public required bool DeclaredOnEntity { get; init; }
}

public enum SchemaFieldClassification
{
    /// <summary>Declared on the entity AND present in every sampled doc.</summary>
    Full = 0,

    /// <summary>Present in some but not all sampled docs (schema drift).</summary>
    Partial = 1,

    /// <summary>Declared on the entity but missing from every sampled doc.</summary>
    EntityOnly = 2,

    /// <summary>Present in samples but not declared on the entity.</summary>
    DocumentOnly = 3,
}
