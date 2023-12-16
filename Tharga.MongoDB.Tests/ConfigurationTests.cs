using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class ConfigurationTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "")]
    [InlineData(null, "SomePart")]
    [InlineData("SomeEnvironment", "SomePart")]
    [InlineData("Production", "SomePart")]
    public Task Basic(string environment, string part)
    {
        //Arrange
        var expectedPart = string.IsNullOrEmpty(part) ? string.Empty : $"_{part}";
        var expectedEnvironment = string.IsNullOrEmpty(environment) || environment == "Production" ? string.Empty :  $"_{environment}";
        var configurationMock = new Mock<IConfiguration>(MockBehavior.Strict);
        configurationMock.Setup(x => x.GetSection(It.IsAny<string>())).Returns((string _) => Mock.Of<IConfigurationSection>());
        var hostEnvironmentMock = new Mock<IHostEnvironment>(MockBehavior.Strict);
        hostEnvironmentMock.SetupGet(x => x.EnvironmentName).Returns(environment);
        var connectionStringBuilder = new MongoUrlBuilder(hostEnvironmentMock.Object);
        var mongoUrlBuilderLoaderMock = new Mock<IMongoUrlBuilderLoader>(MockBehavior.Strict);
        mongoUrlBuilderLoaderMock.Setup(x => x.GetConnectionStringBuilder(It.IsAny<DatabaseContext>()))
            .Returns((DatabaseContext _) => (connectionStringBuilder, () => "mongodb://localhost:27017/Tharga{environment}{part}"));
        var repositoryConfiguration = new Mock<IRepositoryConfiguration>(MockBehavior.Strict);
        var databaseContext = Mock.Of<DatabaseContext>(x => x.DatabasePart == part);
        var databaseOptions = Mock.Of<DatabaseOptions> ();
        var sut = new RepositoryConfigurationInternal(mongoUrlBuilderLoaderMock.Object, repositoryConfiguration.Object, databaseOptions, () => databaseContext, environment);

        //Act
        var result = sut.GetDatabaseUrl();

        //Assert
        result.DatabaseName.Should().Be($"Tharga{expectedEnvironment}{expectedPart}");
        return Task.CompletedTask;
    }
}