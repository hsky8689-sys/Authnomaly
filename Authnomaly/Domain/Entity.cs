namespace Authnomaly.Domain;

public class Entity<T>
{
    private T _id;
    public T Id => _id;
    public Entity(T id)
    {
        _id = id;
    }
    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        return EqualityComparer<T>.Default.Equals(this.Id, (obj as Entity<T>)!.Id);
    }
    public override int GetHashCode()
    {
        return EqualityComparer<T>.Default.GetHashCode(Id!);
    }
}