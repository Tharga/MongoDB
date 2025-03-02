using System;
using System.Threading;
using Bogus;
using MongoDB.Bson;

namespace Tharga.MongoDB.Tests.Support;

public static class TestEntityFactory
{
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public static TestEntity CreateTestEntity()
    {
        return CreateTestEntity(GenerateNewId());
    }

    public static TestEntity CreateTestEntity(ObjectId id)
    {
        var faker = new Faker<TestEntity>()
            .RuleFor(x => x.Id, _ => id)
            .RuleFor(x => x.Value, _ => new Faker().Random.String2(12))
            .RuleFor(x => x.ExtraValue, _ => Guid.NewGuid().ToString());
        return faker.Generate();
    }

    public static TestSubEntity CreateTestSubEntity()
    {
        var faker = new Faker<TestSubEntity>()
            .RuleFor(x => x.Id, _ => ObjectId.GenerateNewId())
            .RuleFor(x => x.Value, _ => new Faker().Random.String2(12))
            .RuleFor(x => x.ExtraValue, _ => Guid.NewGuid().ToString())
            .RuleFor(x => x.OtherValue, _ => Guid.NewGuid().ToString());
        return faker.Generate();
    }

    private static ObjectId GenerateNewId()
    {
        try
        {
            _lock.Wait();
            return ObjectId.GenerateNewId();
        }
        finally
        {
            _lock.Release();
        }
    }
}