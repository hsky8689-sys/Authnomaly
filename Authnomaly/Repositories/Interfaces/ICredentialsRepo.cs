using Authnomaly.Domain;

namespace Authnomaly.Repositories.Interfaces;

public interface ICredentialsRepo : IRepo<AuthCredentials,long>
{
    Task<AuthCredentials> FindByUserId(long userId);
    Task<bool> ChangePassword(long userId, string newPassword);
    Task<bool> ChangeUsername(long userId, string newUsername);
}