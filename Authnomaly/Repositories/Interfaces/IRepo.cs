using Authnomaly.Domain;

namespace Authnomaly.Repositories.Interfaces;

public interface IRepo<TEntity,T> where TEntity : Entity<T>
{
    public Task<TEntity> FindById(T id);
    public Task<bool> Delete(T id);
    public Task<T> Add(TEntity entity);
}