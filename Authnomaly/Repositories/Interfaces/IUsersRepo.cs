using Authnomaly.Domain;

namespace Authnomaly.Repositories.Interfaces;

public interface IUsersRepo : IRepo<User,long>
{
    Task<User> FindByUsername(string username);
    Task<bool> ChangeUsername(long userId, string username);
}