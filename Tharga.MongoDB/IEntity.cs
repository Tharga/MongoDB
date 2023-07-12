namespace Tharga.MongoDB;

public interface IEntity<out TKey>
{
    TKey Id { get; }
}