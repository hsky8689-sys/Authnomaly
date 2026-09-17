using Authnomaly.Domain;
using Authnomaly.Repositories.DatabaseRepositories;
using Authnomaly.Repositories.Interfaces;

namespace Authnomaly.Services;

public sealed class AuthService
{
     private readonly IUsersRepo _usersRepo;
     private readonly ICredentialsRepo _credentialsRepo;
     private readonly ILoginAttemptsRepo _loginAttempts;
     /*
     private readonly IRegisteredApplicationsRepo _registeredApplications;
     */
    public AuthService(IUsersRepo usersRepo,ICredentialsRepo credentialsRepo,ILoginAttemptsRepo loginAttemptsRepo)
    {
        _usersRepo = usersRepo;
        _credentialsRepo = credentialsRepo;
        _loginAttempts = loginAttemptsRepo;
    }
    public void Authenticate()
    {
        
    }
    public async Task<User> Login(string username,string password,LoginAttempt attempt,long applicationId = 1L)
    {
        try
        {
            var goodRepo = _usersRepo as UsersRepository;
            User found = await goodRepo.Login(username, password);
            attempt.Succeeded = found.Id != -1;
            await _loginAttempts.Add(attempt);
            return found;
        }
        catch
        {
            throw;
        }   
    }
}