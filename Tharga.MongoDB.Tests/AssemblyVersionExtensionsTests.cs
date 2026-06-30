using FluentAssertions;
using Tharga.MongoDB.Monitor.Client;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class AssemblyVersionExtensionsTests
{
    [Fact]
    public void GetLibraryVersion_NullAssembly_ReturnsNull()
    {
        System.Reflection.Assembly assembly = null;

        assembly.GetLibraryVersion().Should().BeNull();
    }

    [Fact]
    public void GetLibraryVersion_RealAssembly_ReturnsCleanVersion_WithoutBuildMetadata()
    {
        // The monitor client assembly carries an informational version like "1.0.0+<sha>" (SourceLink);
        // the helper must strip the build-metadata suffix.
        var version = typeof(MonitorClientRegistration).Assembly.GetLibraryVersion();

        version.Should().NotBeNullOrEmpty();
        version.Should().NotContain("+");
    }
}
