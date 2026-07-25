using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Interception;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Interception;

public class InterceptionContractTests
{
    private static CollectionCallInfo BuildCall(InterceptionPoint point = InterceptionPoint.Invocation)
    {
        return new CollectionCallInfo
        {
            CollectionName = nameof(TestEntity),
            Operation = "GetOneAsync",
            OperationType = Operation.Read,
            EntityType = typeof(TestEntity),
            Point = point
        };
    }

    [Fact]
    public void Proceed_IsNotRejected_AndCarriesNoReason()
    {
        var decision = InterceptDecision.Proceed;

        decision.IsRejected.Should().BeFalse();
        decision.Reason.Should().BeNull();
    }

    [Fact]
    public void DefaultDecision_IsProceed()
    {
        // The struct default must be the permissive value — a decision that was never explicitly
        // set must never silently block a database call.
        var decision = default(InterceptDecision);

        decision.Should().Be(InterceptDecision.Proceed);
        decision.IsRejected.Should().BeFalse();
    }

    [Fact]
    public void Reject_IsRejected_AndCarriesTheReason()
    {
        var decision = InterceptDecision.Reject("No team scope in context");

        decision.IsRejected.Should().BeTrue();
        decision.Reason.Should().Be("No team scope in context");
    }

    [Fact]
    public void AccessDeniedException_SurfacesReasonAndCall()
    {
        var call = BuildCall();

        var exception = new CollectionAccessDeniedException("No team scope in context", call);

        exception.Reason.Should().Be("No team scope in context");
        exception.Call.Should().BeSameAs(call);
        exception.Message.Should().Contain("TestEntity.GetOneAsync").And.Contain("No team scope in context");
    }

    [Fact]
    public void Interceptor_DefaultsToInvocationPoint()
    {
        // The common case — a policy gate — should not have to think about timing points.
        ICollectionInterceptor interceptor = new PolicyInterceptor();

        interceptor.Points.Should().Be(InterceptionPoint.Invocation);
    }

    [Fact]
    public void Interceptor_CanDeclareBothPoints()
    {
        ICollectionInterceptor interceptor = new BothPointsInterceptor();

        interceptor.Points.Should().HaveFlag(InterceptionPoint.Invocation);
        interceptor.Points.Should().HaveFlag(InterceptionPoint.Enumeration);
    }

    [Fact]
    public async Task Interceptor_ReceivesThePointItIsCalledFor()
    {
        var interceptor = new BothPointsInterceptor();

        await interceptor.BeforeCallAsync(BuildCall(InterceptionPoint.Invocation), CancellationToken.None);
        await interceptor.BeforeCallAsync(BuildCall(InterceptionPoint.Enumeration), CancellationToken.None);

        interceptor.SeenPoints.Should().Equal(InterceptionPoint.Invocation, InterceptionPoint.Enumeration);
    }

    [Fact]
    public async Task Interceptor_CanRejectFromTheContract()
    {
        ICollectionInterceptor interceptor = new PolicyInterceptor { Allow = false };

        var decision = await interceptor.BeforeCallAsync(BuildCall(), CancellationToken.None);

        decision.IsRejected.Should().BeTrue();
        decision.Reason.Should().Be("denied");
    }

    private class PolicyInterceptor : ICollectionInterceptor
    {
        public bool Allow { get; init; } = true;

        public ValueTask<InterceptDecision> BeforeCallAsync(CollectionCallInfo call, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Allow ? InterceptDecision.Proceed : InterceptDecision.Reject("denied"));
        }
    }

    private class BothPointsInterceptor : ICollectionInterceptor
    {
        private readonly List<InterceptionPoint> _seenPoints = [];

        public InterceptionPoint Points => InterceptionPoint.Invocation | InterceptionPoint.Enumeration;

        public IReadOnlyList<InterceptionPoint> SeenPoints => _seenPoints;

        public ValueTask<InterceptDecision> BeforeCallAsync(CollectionCallInfo call, CancellationToken cancellationToken = default)
        {
            _seenPoints.Add(call.Point);
            return ValueTask.FromResult(InterceptDecision.Proceed);
        }
    }
}
