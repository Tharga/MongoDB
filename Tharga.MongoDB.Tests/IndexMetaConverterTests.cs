using FluentAssertions;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class IndexMetaConverterTests
{
    [Fact]
    public void BuildIndexMetas_NullInstance_ReturnsEmpty()
    {
        // A decorated ICollectionProvider can hand the monitor a DispatchProxy over the collection
        // interface, which is not a RepositoryCollectionBase — the monitor's cast then yields null.
        // BuildIndexMetas must degrade to an empty set rather than dereferencing null (issue #133).
        var result = IndexMetaConverter.BuildIndexMetas(null);

        result.Should().BeEmpty();
    }
}
