using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.MongoDB.Disk;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class ExecuteLimiterLoggingTests
{
    private const string ServerKey = "test-server";
    private const string ConfigName = "test-config";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static ExecuteCallContext Ctx() => new()
    {
        CallKey = Guid.NewGuid(),
        ConfigurationName = ConfigName,
        FunctionName = "test",
        Operation = Operation.Read,
    };

    private static ExecuteLimiter CreateLimiter(ILogger<ExecuteLimiter> logger)
    {
        var options = Mock.Of<IOptions<ExecuteLimiterOptions>>(x =>
            x.Value == new ExecuteLimiterOptions { Enabled = true, MaxConcurrent = 1 });
        return new ExecuteLimiter(options, logger);
    }

    private static void VerifyLogged(Mock<ILogger<ExecuteLimiter>> logger, LogLevel level, Times times)
    {
        logger.Verify(x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString().Contains("Queued")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            times);
    }

    [Fact]
    public async Task QueuedExecutions_message_is_logged_at_Debug_not_Information()
    {
        var logger = new Mock<ILogger<ExecuteLimiter>>();
        var limiter = CreateLimiter(logger.Object);

        // Block the single concurrency slot so the next two operations pile up in the queue,
        // driving queuedCount past the > 1 threshold that emits the "Queued ..." message.
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocking = limiter.ExecuteAsync(async _ => { await gate.Task; return 0; }, ServerKey, 100, Ctx(), CancellationToken.None);
        await SpinUntil(() => limiter.GetCurrentState().ExecutingCount == 1);

        var waiterA = limiter.ExecuteAsync(_ => Task.FromResult(1), ServerKey, 100, Ctx(), CancellationToken.None);
        var waiterB = limiter.ExecuteAsync(_ => Task.FromResult(2), ServerKey, 100, Ctx(), CancellationToken.None);
        await SpinUntil(() => limiter.GetCurrentState().QueueCount >= 2);

        gate.SetResult(0);
        await Task.WhenAll(blocking, waiterA, waiterB).WaitAsync(Timeout);

        VerifyLogged(logger, LogLevel.Debug, Times.AtLeastOnce());
        VerifyLogged(logger, LogLevel.Information, Times.Never());
    }

    private static async Task SpinUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met within timeout.");
            await Task.Delay(5);
        }
    }
}
