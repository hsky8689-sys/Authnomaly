using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using Authnomaly.Utils;

namespace Authnomaly.Services;

public sealed class AuthService
{
     private readonly IUsersRepo _usersRepo;
     private readonly ICredentialsRepo _credentialsRepo;
     private readonly ILoginAttemptsRepo _loginAttempts;
     private readonly ISigningKeyStore _keyStore;
     /*
     private readonly IRegisteredApplicationsRepo _registeredApplications;
     */
    public AuthService(IUsersRepo usersRepo,
                       ICredentialsRepo credentialsRepo,
                       ILoginAttemptsRepo loginAttemptsRepo,
                       ISigningKeyStore signingKeyRepo)
    {
        _usersRepo = usersRepo;
        _credentialsRepo = credentialsRepo;
        _loginAttempts = loginAttemptsRepo;
        _keyStore = signingKeyRepo;
    }
    public async Task<(bool authenticated,string message)> Authenticate(string username,string password,string email)
    {
        (byte[] hash, byte[] salt) hashedPasswordData = ([],[]);
        try
        {
            bool authenticated = true;
            string message = "You were succesfully logged in";
            User? found = await _usersRepo.FindByUsername(username);
            if (found.Id != 0)
            {
                message = "User with given username already exists";
                authenticated = false;
                return (authenticated, message);
            }

            User newUser = new User(0, username, email);
            if (await _usersRepo.Add(newUser) == 0)
            {
                message = "User with given username already exists";
                authenticated = false;
                return (authenticated, message);
            }
            hashedPasswordData = Encryption.HashPassword(password);
            AuthCredentials credentials = new AuthCredentials(0, username,
                Convert.ToBase64String(hashedPasswordData.hash), newUser, hashedPasswordData.salt);
            if (await _credentialsRepo.Add(credentials) == 0)
            {
                message = "User with given username already exists";
                authenticated = false;
                return (authenticated, message);
            }
            return (authenticated, message);
        }
        finally
        {
            Encryption.DeleteFromRam(hashedPasswordData.hash, hashedPasswordData.salt);
        }
    }
    public async Task<User> Login(string username,string password,LoginAttempt attempt,long applicationId = 1L)
    {
        try
        {
            User? found = await _usersRepo.FindByUsername(username);
            var credentials = await _credentialsRepo.FindByUserId(found.Id);
            var ok = Encryption.VerifyPassword(password, Convert.FromBase64String(credentials.PasswordHash), credentials.Salt);
            attempt.Succeeded = ok;
            await _loginAttempts.Add(attempt);
            return ok ? found : new User(0, "", "");
        }
        catch
        {
            throw;
        }   
    }
}