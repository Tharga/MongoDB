using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;
using Tharga.MongoDB.Tests.Lockable.Base;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable;

/// <summary>
/// Coverage for GitHub issue #198. ExecuteAsync(execute, operation, cancellationToken) passed CancellationToken.None on,
/// so the caller's token was never observed by the execute limiter nor handed to the execute delegate.
/// </summary>
public class ExecuteAsyncCancellationTests : LockableTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task ExecuteAsync_WithCancelledToken_CancelsTheDriverCall()
    {
        // With a free execute-limiter slot the limiter itself does not observe the token, so the observable effect
        // is that the token reaches the execute delegate and, through it, the driver.
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = () => sut.ExecuteAsync((col, ct) => col.CountDocumentsAsync(FilterDefinition<LockableTestEntity>.Empty, cancellationToken: ct), Operation.Read, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    [Trait("Category", "Database")]
    public async Task ExecuteAsync_PassesCallersTokenToExecute()
    {
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        using var cancellation = new CancellationTokenSource();
        var received = CancellationToken.None;

        await sut.ExecuteAsync((col, ct) =>
        {
            received = ct;
            return Task.FromResult(true);
        }, Operation.Read, cancellation.Token);

        received.CanBeCanceled.Should().BeTrue();
        await cancellation.CancelAsync();
        received.IsCancellationRequested.Should().BeTrue();
    }
}
