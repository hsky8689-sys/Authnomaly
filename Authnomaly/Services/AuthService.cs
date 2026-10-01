using System.Text;
using System.Transactions;
using System.Xml;
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
     private readonly ILogger<AuthService> _logger;
     /*
     private readonly IRegisteredApplicationsRepo _registeredApplications;
     */
    public AuthService(IUsersRepo usersRepo,
                       ICredentialsRepo credentialsRepo,
                       ILoginAttemptsRepo loginAttemptsRepo,
                       ISigningKeyStore signingKeyRepo,ILogger<AuthService> logger)
    {
        _usersRepo = usersRepo;
        _credentialsRepo = credentialsRepo;
        _loginAttempts = loginAttemptsRepo;
        _keyStore = signingKeyRepo;
        _logger = logger;
    }
    public async Task<(bool authenticated,string message)> Authenticate(string username,string password,string email)
    {
        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);
        string callData = $"Function called with username:{username}/password:{password}/email:{email}";
        bool authenticated = false;
        string message = "User wasn't created";
        (byte[] hash, byte[] salt) hashedPasswordData = ([],[]);
        try
        {
            var lastId = await _usersRepo.Add(new User(0, username, email));
            if (lastId == 0)
            {
                _logger.LogError("User wasn't added");
                return (authenticated, "User with given username already exists");
            }
            _logger.LogDebug(new StringBuilder(callData).Append($"Last added user id:{lastId}").ToString());
            hashedPasswordData = Encryption.HashPassword(password);
            User newUser = new User(lastId, username, email);
            AuthCredentials credentials = new AuthCredentials(lastId,
                Convert.ToBase64String(hashedPasswordData.hash), newUser, hashedPasswordData.salt);
            var lastCredsId = await _credentialsRepo.Add(credentials);
            if (lastCredsId == 0)
            {
                _logger.LogError("Credentials weren't added");
                return (authenticated, message);
            }
            _logger.LogDebug(new StringBuilder(callData).Append($"Last added user credentials id:{lastCredsId}")
                .ToString());
            authenticated = true;
            message = "Your account has been succesfully created";
            scope.Complete();
            return (authenticated, message);
        }
        catch(Exception e)
        {
            authenticated = false;
            message = "Your account could not be created";
            _logger.LogCritical(new StringBuilder(callData).Append($"Certain repository throws error:{e.StackTrace}").ToString());
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
    public async Task<bool> ChangeUsername(long userId, string oldUsername,string newUsername)
    {
        return await _usersRepo.ChangeUsername(userId, oldUsername,newUsername);
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