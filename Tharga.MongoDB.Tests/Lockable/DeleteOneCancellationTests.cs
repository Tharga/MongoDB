using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MongoDB.Bson;
using Tharga.MongoDB.Tests.Lockable.Base;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests.Lockable;

public class DeleteOneCancellationTests : LockableTestBase
{
    [Fact]
    [Trait("Category", "Database")]
    public async Task DeleteOneAsync_WithCancelledToken_ThrowsBeforeLocking()
    {
        //Arrange
        var sut = new LockableTestRepositoryCollection(_mongoDbServiceFactory);
        var entity = new LockableTestEntity { Id = ObjectId.GenerateNewId() };
        await sut.AddAsync(entity);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        //Act
        var act = () => sut.DeleteOneAsync(entity.Id, cancellationToken: cancellation.Token);

        //Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        var stored = await sut.GetOneAsync(entity.Id);
        stored.Should().NotBeNull();
        stored.Lock.Should().BeNull("the token is checked before the pick, so the document is never locked");
    }
}
