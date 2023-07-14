namespace Tharga.MongoDB.Tests.Support;

public record TestSubEntity : TestEntity
{
    public string OtherValue { get; set; }
}