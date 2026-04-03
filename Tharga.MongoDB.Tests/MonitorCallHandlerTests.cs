using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Tharga.MongoDB.Monitor.Client;
using Tharga.MongoDB.Monitor.Server;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class MonitorCallHandlerTests
{
    [Fact]
    public async Task Handle_IngestsCallIntoMonitor()
    {
        // Arrange
        var monitorMock = new Mock<IDatabaseMonitor>();
        CallDto captured = null;
        monitorMock.Setup(m => m.IngestCall(It.IsAny<CallDto>()))
            .Callback<CallDto>(c => captured = c);

        var sut = new MonitorCallHandler(monitorMock.Object);
        var callDto = new CallDto
        {
            Key = Guid.NewGuid(),
            StartTime = DateTime.UtcNow,
            SourceName = "RemoteAgent",
            ConfigurationName = "Default",
            DatabaseName = "RemoteDb",
            CollectionName = "Orders",
            FunctionName = "GetAsync",
            Operation = "Read",
            ElapsedMs = 42,
            Count = 10,
            Final = true
        };
        var message = new MonitorCallMessage { Call = callDto };

        // Act
        await sut.Handle(message);

        // Assert
        monitorMock.Verify(m => m.IngestCall(It.IsAny<CallDto>()), Times.Once);
        captured.Should().NotBeNull();
        captured.Key.Should().Be(callDto.Key);
        captured.SourceName.Should().Be("RemoteAgent");
        captured.DatabaseName.Should().Be("RemoteDb");
    }
}
