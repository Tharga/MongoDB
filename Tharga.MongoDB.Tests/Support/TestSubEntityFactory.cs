//using System;
//using System.Collections.Generic;
//using Bogus;
//using MongoDB.Bson;

//namespace Tharga.MongoDB.Tests.Support;

//public static class TestSubEntityFactory
//{
//    private static readonly Faker<TestSubEntity> _faker = new Faker<TestSubEntity>()
//        .RuleFor(x => x.Id, _ => ObjectId.GenerateNewId()) // Generate new ObjectId
//        .RuleFor(x => x.Value, f => Guid.NewGuid().ToString())
//        .RuleFor(x => x.ExtraValue, f => Guid.NewGuid().ToString())
//        .RuleFor(x => x.OtherValue, f => Guid.NewGuid().ToString());

//    public static TestSubEntity Create()
//    {
//        return _faker.Generate();
//    }

//    public static TestSubEntity Create(ObjectId id)
//    {
//        return _faker.RuleFor(x => x.Id, id).Generate();
//    }

//    public static List<TestSubEntity> CreateMany(int count)
//    {
//        return _faker.Generate(count);
//    }
//}