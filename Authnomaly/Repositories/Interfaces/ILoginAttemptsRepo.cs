using Authnomaly.Domain;

namespace Authnomaly.Repositories.Interfaces;

public interface ILoginAttemptsRepo:IRepo<LoginAttempt,long>
{
    Task<IList<LoginAttempt>> FindByUsername(string username);
}