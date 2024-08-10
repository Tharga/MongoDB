using System.Threading.Tasks;
using Tharga.MongoDB.Tests.Support;
using Xunit;

namespace Tharga.MongoDB.Tests;

public class LockableRepositoryCollectionTests
{
    [Fact]
    public async Task Update()
    {
        ////Arrange
        var collection = new LockableTestRepositoryCollection();
        //var entity = new TestEntity { Id = ObjectId.GenerateNewId(), Value = "Value" };
        //await collection.AddAsync(entity);

        //// Act
        //var result = await collection.UpdateAsync(entity.Id, x => x.Value, "NewValue");

        //// Assert
        //result.Before.Should().Be(entity);
        //(await collection.GetAsync(x => x.Id == entity.Id).ToArrayAsync()).First().Value.Should().Be("NewValue");
    }
}