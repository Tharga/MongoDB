using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Tharga.Communication.Client.Communication;
using Tharga.MongoDB.Configuration;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Monitor.Client;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class MonitorForwarderTests : IAsyncLifetime
{
    private readonly Mock<IMongoDbServiceFactory> _factoryMock;
    private readonly Mock<IDatabaseMonitor> _monitorMock;
    private readonly Mock<IClientCommunication> _clientMock;
    private readonly MonitorForwarder _sut;

    private EventHandler<CallStartEventArgs> _callStartHandler;
    private EventHandler<CallEndEventArgs> _callEndHandler;

    public MonitorForwarderTests()
    {
        _factoryMock = new Mock<IMongoDbServiceFactory>();
        _monitorMock = new Mock<IDatabaseMonitor>();
        _clientMock = new Mock<IClientCommunication>();

        _factoryMock.SetupAdd(f => f.CallStartEvent += It.IsAny<EventHandler<CallStartEventArgs>>())
            .Callback<EventHandler<CallStartEventArgs>>(h => _callStartHandler = h);
        _factoryMock.SetupAdd(f => f.CallEndEvent += It.IsAny<EventHandler<CallEndEventArgs>>())
            .Callback<EventHandler<CallEndEventArgs>>(h => _callEndHandler = h);

        _sut = new MonitorForwarder(_factoryMock.Object, _monitorMock.Object, _clientMock.Object);
    }

    public async Task InitializeAsync()
    {
        await _sut.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _sut.StopAsync(CancellationToken.None);
        _sut.Dispose();
    }

    [Fact]
    public async Task FinalCallEnd_ForwardsCallDto()
    {
        // Arrange
        var callKey = Guid.NewGuid();
        var fingerprint = new CollectionFingerprint
        {
            ConfigurationName = "Default",
            DatabaseName = "TestDb",
            CollectionName = "TestCollection"
        };
        var startArgs = new CallStartEventArgs(callKey, fingerprint, "GetAsync", Operation.Read, "TestSource");
        var endArgs = new CallEndEventArgs(callKey, TimeSpan.FromMilliseconds(42), null, 5, final: true);

        MonitorCallMessage captured = null;
        _clientMock.Setup(c => c.IsConnected).Returns(true);
        _clientMock.Setup(c => c.PostAsync(It.IsAny<MonitorCallMessage>()))
            .Callback<MonitorCallMessage>(m => captured = m)
            .Returns(Task.CompletedTask);

        // Act
        _callStartHandler.Invoke(this, startArgs);
        _callEndHandler.Invoke(this, endArgs);
        await Task.Delay(50); // Allow fire-and-forget to complete

        // Assert
        captured.Should().NotBeNull();
        captured.Call.Key.Should().Be(callKey);
        captured.Call.SourceName.Should().Be("TestSource");
        captured.Call.ConfigurationName.Should().Be("Default");
        captured.Call.DatabaseName.Should().Be("TestDb");
        captured.Call.CollectionName.Should().Be("TestCollection");
        captured.Call.FunctionName.Should().Be("GetAsync");
        captured.Call.Operation.Should().Be("Read");
        captured.Call.ElapsedMs.Should().BeApproximately(42, 0.1);
        captured.Call.Count.Should().Be(5);
        captured.Call.Exception.Should().BeNull();
        captured.Call.Final.Should().BeTrue();
    }

    [Fact]
    public async Task NonFinalCallEnd_DoesNotForward()
    {
        // Arrange
        var callKey = Guid.NewGuid();
        var fingerprint = new CollectionFingerprint
        {
            ConfigurationName = "Default",
            DatabaseName = "TestDb",
            CollectionName = "TestCollection"
        };
        var startArgs = new CallStartEventArgs(callKey, fingerprint, "GetAsync", Operation.Read);
        var endArgs = new CallEndEventArgs(callKey, TimeSpan.FromMilliseconds(10), null, 1, final: false);

        _clientMock.Setup(c => c.IsConnected).Returns(true);

        // Act
        _callStartHandler.Invoke(this, startArgs);
        _callEndHandler.Invoke(this, endArgs);
        await Task.Delay(50);

        // Assert
        _clientMock.Verify(c => c.PostAsync(It.IsAny<MonitorCallMessage>()), Times.Never);
    }

    [Fact]
    public async Task NotConnected_DoesNotSend()
    {
        // Arrange
        var callKey = Guid.NewGuid();
        var fingerprint = new CollectionFingerprint
        {
            ConfigurationName = "Default",
            DatabaseName = "TestDb",
            CollectionName = "TestCollection"
        };
        var startArgs = new CallStartEventArgs(callKey, fingerprint, "GetAsync", Operation.Read);
        var endArgs = new CallEndEventArgs(callKey, TimeSpan.FromMilliseconds(10), null, 1, final: true);

        _clientMock.Setup(c => c.IsConnected).Returns(false);

        // Act
        _callStartHandler.Invoke(this, startArgs);
        _callEndHandler.Invoke(this, endArgs);
        await Task.Delay(50);

        // Assert
        _clientMock.Verify(c => c.PostAsync(It.IsAny<MonitorCallMessage>()), Times.Never);
    }

    [Fact]
    public async Task ForwardingFailure_DoesNotThrow()
    {
        // Arrange
        var callKey = Guid.NewGuid();
        var fingerprint = new CollectionFingerprint
        {
            ConfigurationName = "Default",
            DatabaseName = "TestDb",
            CollectionName = "TestCollection"
        };
        var startArgs = new CallStartEventArgs(callKey, fingerprint, "GetAsync", Operation.Read);
        var endArgs = new CallEndEventArgs(callKey, TimeSpan.FromMilliseconds(10), null, 1, final: true);

        _clientMock.Setup(c => c.IsConnected).Returns(true);
        _clientMock.Setup(c => c.PostAsync(It.IsAny<MonitorCallMessage>()))
            .ThrowsAsync(new InvalidOperationException("Connection lost"));

        // Act — should not throw
        _callStartHandler.Invoke(this, startArgs);
        _callEndHandler.Invoke(this, endArgs);
        await Task.Delay(50);

        // Assert — the call was attempted
        _clientMock.Verify(c => c.PostAsync(It.IsAny<MonitorCallMessage>()), Times.Once);
    }

    [Fact]
    public async Task CallEndWithSteps_ForwardsSteps()
    {
        // Arrange
        var callKey = Guid.NewGuid();
        var fingerprint = new CollectionFingerprint
        {
            ConfigurationName = "Default",
            DatabaseName = "TestDb",
            CollectionName = "TestCollection"
        };
        var startArgs = new CallStartEventArgs(callKey, fingerprint, "GetAsync", Operation.Read);
        var steps = new List<CallStepInfo>
        {
            new() { Step = "Queue", Delta = TimeSpan.FromMilliseconds(2) },
            new() { Step = "FetchCollectionAsync", Delta = TimeSpan.FromMilliseconds(10), Message = "Initiated collection." }
        };
        var endArgs = new CallEndEventArgs(callKey, TimeSpan.FromMilliseconds(42), null, 5, steps, final: true);

        MonitorCallMessage captured = null;
        _clientMock.Setup(c => c.IsConnected).Returns(true);
        _clientMock.Setup(c => c.PostAsync(It.IsAny<MonitorCallMessage>()))
            .Callback<MonitorCallMessage>(m => captured = m)
            .Returns(Task.CompletedTask);

        // Act
        _callStartHandler.Invoke(this, startArgs);
        _callEndHandler.Invoke(this, endArgs);
        await Task.Delay(50);

        // Assert
        captured.Should().NotBeNull();
        captured.Call.Steps.Should().HaveCount(2);
        captured.Call.Steps[0].Step.Should().Be("Queue");
        captured.Call.Steps[0].DeltaMs.Should().BeApproximately(2, 0.1);
        captured.Call.Steps[1].Step.Should().Be("FetchCollectionAsync");
        captured.Call.Steps[1].Message.Should().Be("Initiated collection.");
    }

    [Fact]
    public async Task CallEndWithException_ForwardsExceptionMessage()
    {
        // Arrange
        var callKey = Guid.NewGuid();
        var fingerprint = new CollectionFingerprint
        {
            ConfigurationName = "Default",
            DatabaseName = "TestDb",
            CollectionName = "TestCollection"
        };
        var startArgs = new CallStartEventArgs(callKey, fingerprint, "UpdateOneAsync", Operation.Update);
        var endArgs = new CallEndEventArgs(callKey, TimeSpan.FromMilliseconds(100),
            new InvalidOperationException("Duplicate key"), 0, final: true);

        MonitorCallMessage captured = null;
        _clientMock.Setup(c => c.IsConnected).Returns(true);
        _clientMock.Setup(c => c.PostAsync(It.IsAny<MonitorCallMessage>()))
            .Callback<MonitorCallMessage>(m => captured = m)
            .Returns(Task.CompletedTask);

        // Act
        _callStartHandler.Invoke(this, startArgs);
        _callEndHandler.Invoke(this, endArgs);
        await Task.Delay(50);

        // Assert
        captured.Should().NotBeNull();
        captured.Call.Exception.Should().Be("Duplicate key");
        captured.Call.Operation.Should().Be("Update");
    }
}
