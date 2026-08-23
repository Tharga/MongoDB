using FluentAssertions;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Regression coverage for GitHub issue #147. The collection stats command was built as an
/// interpolated json string with a lowercase command name, <c>{collstats: 'Name'}</c>. A real
/// MongoDB server dispatches command names case-insensitively and accepts it, but Azure Cosmos DB
/// for MongoDB then requires the element spelled exactly <c>collStats</c> and fails with
/// "Required element collStats missing". Building a document also removes the escaping hazard the
/// interpolated string had, since a collection name may contain a quote.
/// </summary>
public class Issue147CollStatsCommandTests
{
    [Fact]
    public void BuildCollStatsCommand_UsesCamelCaseCommandName()
    {
        var command = MongoDbService.BuildCollStatsCommand("MyCollection");

        command.ElementCount.Should().Be(1);
        command.GetElement(0).Name.Should().Be("collStats");
        command["collStats"].AsString.Should().Be("MyCollection");
    }

    [Fact]
    public void BuildCollStatsCommand_WithQuoteInCollectionName_KeepsTheNameIntact()
    {
        var command = MongoDbService.BuildCollStatsCommand("it's");

        command["collStats"].AsString.Should().Be("it's");
    }
}
