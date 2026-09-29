using Authnomaly.Domain;

namespace Authnomaly.Repositories.Interfaces;

public interface ICredentialsRepo : IRepo<AuthCredentials,long>
{
    Task<AuthCredentials> FindByUserId(long userId);
    Task<bool> ChangePassword(long userId, string newPasswordHash,
                              byte[] newPasswordSalt,string oldPasswordHash,
                              byte[] oldPasswordSalt );
}