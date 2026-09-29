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
            string message = "Your account has been succesfully created";
            User found = await _usersRepo.FindByUsername(username);
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
            AuthCredentials credentials = new AuthCredentials(0,
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
    public async Task<bool> ChangePassword(long userId, string newPassword)
    {
        var oldCredentials = await _credentialsRepo.FindByUserId(userId);
        if (oldCredentials.Id==0) return false;
        (byte[] newHash, byte[] newSalt) hashed = Encryption.HashPassword(newPassword);
        return await _credentialsRepo.ChangePassword(userId,
                                      Convert.ToBase64String(hashed.newHash),
                                                    hashed.newSalt,oldCredentials.PasswordHash,
                                                    oldCredentials.Salt);
    }
    public async Task<User> Login(string username,string password,LoginAttempt attempt,long applicationId = 1L)
    {
        try
        {
            User found = await _usersRepo.FindByUsername(username);
            if (found.Id == 0) 
                return new User(0, "", "");
            var credentials = await _credentialsRepo.FindByUserId(found.Id);
            if (credentials.Id == 0) 
                return new User(0, "", "");
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