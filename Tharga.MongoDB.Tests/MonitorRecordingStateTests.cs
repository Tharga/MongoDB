using FluentAssertions;
using Tharga.MongoDB;
using Tharga.MongoDB.Configuration;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class MonitorRecordingStateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Full_AlwaysRecordsEverything(bool consumed)
    {
        var state = new MonitorRecordingState { Level = CallRecordingLevel.Full, CallsConsumed = consumed };
        state.ShouldRecord.Should().BeTrue();
        state.ShouldRecordSteps.Should().BeTrue();
    }

    [Fact]
    public void OnDemand_RecordsCall_ButStepsOnlyWhenConsumed()
    {
        var idle = new MonitorRecordingState { Level = CallRecordingLevel.OnDemand, CallsConsumed = false };
        idle.ShouldRecord.Should().BeTrue();        // keep the lightweight call
        idle.ShouldRecordSteps.Should().BeFalse();  // but not the step timeline

        var watched = new MonitorRecordingState { Level = CallRecordingLevel.OnDemand, CallsConsumed = true };
        watched.ShouldRecord.Should().BeTrue();
        watched.ShouldRecordSteps.Should().BeTrue();
    }

    [Fact]
    public void WhenConsumed_RecordsNothingUntilConsumed()
    {
        var idle = new MonitorRecordingState { Level = CallRecordingLevel.WhenConsumed, CallsConsumed = false };
        idle.ShouldRecord.Should().BeFalse();
        idle.ShouldRecordSteps.Should().BeFalse();

        var watched = new MonitorRecordingState { Level = CallRecordingLevel.WhenConsumed, CallsConsumed = true };
        watched.ShouldRecord.Should().BeTrue();
        watched.ShouldRecordSteps.Should().BeTrue();
    }

    [Fact]
    public void DefaultLevel_IsOnDemand()
    {
        new MonitorRecordingState().Level.Should().Be(CallRecordingLevel.OnDemand);
    }
}
