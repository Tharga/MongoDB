using MongoDB.Bson;

namespace Tharga.MongoDB.ConsoleSample.SampleRepo;

public abstract record MyBaseEntity : EntityBase<ObjectId>
{
    public string Value { get; init; }
}

public record MyEntity : MyBaseEntity
{
}

public record MyOtherEntity : MyBaseEntity
{
    public override void EndInit()
    {
        if (CatchAll != null)
            CatchAll.Clear();

        base.EndInit();
    }
}