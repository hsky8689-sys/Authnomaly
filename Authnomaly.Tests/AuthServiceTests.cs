using System.Text.RegularExpressions;
using Authnomaly.Domain;
using Authnomaly.Repositories.DatabaseRepositories;
using Authnomaly.Repositories.Interfaces;
using Authnomaly.Services;
using Authnomaly.Utils;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Authnomaly.Tests;

public class AuthServiceTests
{
    private readonly IDataProtector _protector;
     private readonly ITestOutputHelper _output;
     private readonly Mock<IUsersRepo> _usersMock;
     private readonly Mock<ICredentialsRepo> _credentialsMock;
     private readonly Mock<ILoginAttemptsRepo> _loginAttemptsMock;
     private HashSet<LoginAttempt> _loginAttemptsMemoryMock;
     private readonly int _knownUsers;
     private readonly TestDb _db = new(); 
    private readonly AuthService _service;
    public AuthServiceTests(ITestOutputHelper output)
    {
        _knownUsers = 10;
        _protector = DataProtectionProvider.Create("test-app").CreateProtector("Authnomaly.SigningKeys");
        _output = output;
        _usersMock = new Mock<IUsersRepo>();
        _credentialsMock = new Mock<ICredentialsRepo>();
        _loginAttemptsMock = new Mock<ILoginAttemptsRepo>();
        _loginAttemptsMemoryMock = new HashSet<LoginAttempt>();
        _loginAttemptsMock.Setup(x => x.FindByUsername(It.IsAny<string>()))
            .ReturnsAsync((string username) =>
            {
                return _loginAttemptsMemoryMock.Where(la=>la.Username.Equals(username)).ToList(); 
            });
        _loginAttemptsMock.Setup(x => x.Add(It.IsAny<LoginAttempt>()))
            .ReturnsAsync((LoginAttempt la) =>
            {
                if (_loginAttemptsMemoryMock.Contains(la)) return 0;
                else
                {
                    _loginAttemptsMemoryMock.Add(la);
                    return la.Id;
                }
            });
        var usersById = new Dictionary<long, User>();
        var credentialsById = new Dictionary<long, AuthCredentials>();
        _credentialsMock.Setup(cm => cm.FindByUserId(It.IsAny<long>())).ReturnsAsync((long id) =>
            credentialsById.TryGetValue(id, out var found) ? found : new AuthCredentials(0));
        _usersMock.Setup(u => u.Add(It.IsAny<User>())).ReturnsAsync((User entity) =>
        {
            if (entity.Id == 0) return _knownUsers + 1;
            return entity.Id > _knownUsers ? entity.Id : 0;
        });
        _usersMock.Setup(u => u.Delete(It.IsAny<long>())).ReturnsAsync((long id) =>
        { return id <= _knownUsers;});
        _credentialsMock.Setup(cm => cm.Add(It.IsAny<AuthCredentials>())).ReturnsAsync((AuthCredentials entity) =>
        {
            if (entity.Id == 0) return _knownUsers + 1;
            return entity.Id > _knownUsers ? entity.Id : 0; 
        });
        _credentialsMock.Setup(cm => cm.FindByUserId(It.IsAny<long>())).Returns(async (long id) =>
        {
            return 1 <= id && id <= _knownUsers ? credentialsById[id] : new AuthCredentials(0);
        });
        _usersMock.Setup(u => u.FindByUsername(It.IsAny<string>())).ReturnsAsync((string username) =>
        {
            var match = Regex.Match(username, @"^user(\d+)$");
            if (!match.Success) return new User(0, "", "");
            long parsedId = long.Parse(match.Groups[1].Value);
            return usersById.TryGetValue(parsedId, out var found) ? found : new User(0, "", "");
        });
        long idLoginAttempt = 0;
        for (int i = 1; i <= _knownUsers; i++)
        {
            long id = Convert.ToInt64(i);
            var currentUser = new User(id, $"user{id}", "10-10-20202");
            usersById[id] = currentUser;
            var encrypted = Encryption.HashPassword($"pass{id}");
            var salt = encrypted.Salt;
            var hashed = encrypted.Hash;
            var userCredentials = new AuthCredentials(id,Convert.ToBase64String(hashed),currentUser,salt);

            _usersMock.Setup(x => x.FindById(id)).ReturnsAsync(currentUser);

            _credentialsMock.Setup(x => x.FindByUserId(id)).ReturnsAsync((long id)=>
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(1));
                _output.WriteLine("password retrieved at "+DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                return userCredentials;
            });
            _credentialsMock.Setup(x => x.FindById(id)).ReturnsAsync(userCredentials);
            _credentialsMock.Setup(x => x.ChangePassword(id,It.IsAny<string>(),It.IsAny<byte[]>(),It.IsAny<string>(),It.IsAny<byte[]>())).ReturnsAsync((long _id,
                string _newPassword,byte[] salt,string oldPassword,byte[] oldSalt) =>
            {
                
                userCredentials.PasswordHash = _newPassword;
                return true;
            });
            for (int j = 0; j <= i % 3; j++)
            {
                var crt = idLoginAttempt;
                var attempt = new LoginAttempt(idLoginAttempt);
                attempt.Username = $"user{i}";
                attempt.Succeeded = j == i % 3;
                _loginAttemptsMock.Object.Add(attempt);
                _loginAttemptsMock.Setup(x => x.FindById(crt)).Returns(Task.FromResult(attempt));
                idLoginAttempt++;
            }
        }
        _service = new AuthService(_usersMock.Object,_credentialsMock.Object,_loginAttemptsMock.Object,new Mock<ISigningKeyStore>().Object,new XunitLogger<AuthService>(_output));
    }
    [Fact]
    public async Task BasicLoginTests()
    {
        // right/wrong credentials for testing
        int logged = 0;
        for (int i = 1; i <= 100; i++)
        {
            try
            {
                if ((await _service.Login($"user{i}", $"pass{i}", new LoginAttempt(0))).Id != 0)
                    logged++;
            }
            catch
            {
                continue;
            }
        }
        Assert.True(logged==_knownUsers);
        //login attempts saved after constructor + previous iterations
        for (int i = 1; i <= 100; i++)
        {
            var foundAttempts = await _loginAttemptsMock.Object.FindByUsername($"user{i}");
            var succeededAttempts = (from la in foundAttempts where la.Succeeded==true select la) ?? new List<LoginAttempt>();
            Assert.True(i<=_knownUsers 
                          ? 
                          succeededAttempts.Count()==1 && foundAttempts.Count() == i%3+1
                          :succeededAttempts.Count()==0 && foundAttempts.Count() == 0);
        }
    }
    [Fact]
    public async Task SavedCorrectHashesTest()
    {
        for (int i = 1; i <= _knownUsers; i++)
        {
            var savedData = await _credentialsMock.Object.FindByUserId(i);
            Assert.True(Encryption.VerifyPassword($"pass{i}",Convert.FromBase64String(savedData.PasswordHash),savedData.Salt));
        }
        for (int i = _knownUsers+1; i <= 2*_knownUsers; i++)
        {
            var savedData = await _credentialsMock.Object.FindByUserId(i);
            Assert.True(savedData.Id==0);
        }
    }
    [Fact]
    public async Task BasicSignUpTests()
    {
        for (int i = 1; i <= _knownUsers; i++)
        {
            var result = await _service.Authenticate($"user{i}", $"pass{i}", $"gmail@user{i}.com"); 
            Assert.False(result.authenticated);
            Assert.Equal("User with given username already exists",result.message);
        }
        //able to add new users to the app
        for (int i = _knownUsers + 1; i <= 2 * _knownUsers; i++)
        {
            var result = await _service.Authenticate($"user{i}", $"pass{i}", $"gmail@user{i}.com"); 
            Assert.True(result.authenticated);
            Assert.Equal("Your account has been succesfully created",result.message);
        }
        //credentials saved for new users
        for (int i = 1; i <= 2 * _knownUsers; i++)
        {
            var credentials = await _credentialsMock.Object.FindByUserId(i);
            Assert.True(credentials.Id!=0 == i <=_knownUsers);
        }
    }
    [Fact]
    public async Task UserCredentialsChangeDuringLoginTests()
    {
        var user = await _usersMock.Object.FindByUsername($"user1");
        List<Thread> loginThreads = new List<Thread>();
        List<Thread> userDeleters = new List<Thread>();
        int counter = 0;//ok attempts(user could not log in with old credentials while new ones were changed)
        (byte[] oldPasswordHash, byte[] oldPasswordSalt) oldPassData = Encryption.HashPassword("pass1");
        (byte[] newPasswordHash, byte[] newPasswordSalt) newPassData = Encryption.HashPassword("pass2");
            //tests to verify race conditions that may alter the application's inner logic if
            //a login attempt succeedes on,let's say a user changes his password,the old login
            //request should not be accepted , given the fact that it contains old data
            var loginAttempt = new LoginAttempt(0);
            loginAttempt.Username = "user1";
                try
                {
                    loginThreads.Add(new Thread(async () =>
                    {
                        _output.WriteLine("login started "+DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                        if((await _service.Login($"user1", $"pass1", loginAttempt)).Id == 0)
                            Interlocked.Increment(ref counter);
                        _output.WriteLine("login finished "+DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    }));
                    userDeleters.Add(new Thread(async () =>
                    {
                        await _credentialsMock.Object.ChangePassword(user.Id, 
                                                       Convert.ToBase64String(newPassData.newPasswordHash),
                                                                     newPassData.newPasswordSalt,
                                                       Convert.ToBase64String(oldPassData.oldPasswordHash),
                                                                     oldPassData.oldPasswordSalt);
                        _output.WriteLine("password succesfully changed at "+DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    }));
                    loginThreads[0].Start();
                    userDeleters[0].Start();
                }
                finally
                {
                    loginThreads[0].Join();
                    userDeleters[0].Join();
                    await _credentialsMock.Object.ChangePassword(user.Id, 
                                                  Convert.ToBase64String(oldPassData.oldPasswordHash),
                                                                oldPassData.oldPasswordSalt,
                                                  Convert.ToBase64String(newPassData.newPasswordHash),
                                                                newPassData.newPasswordSalt);
                }
        _output.WriteLine(counter.ToString());
        Assert.True(counter == 1);
    }
    [Theory]
    [InlineData(200,false)]
    public async Task ConcurrentSignupnWithSameUsernameTests(int threads,bool fromSameContext)
    {
        using var context = _db.NewScope();
        var singleContextMockService = MakeUnmockedService(context);
        try
        {
            const string username = "user2313";
            const string password = "oewqeqwe";
            const string email = "mail@scs.carrefour.ro";
            int failsCounter = 0;
            var taskList = fromSameContext
                ? Enumerable.Range(0, threads).Select(_ => singleContextMockService.Authenticate(username, password, email))
                : Enumerable.Range(0, threads).Select(_ => MakeUnmockedService(_db.NewScope()).Authenticate(username, password, email));
            foreach (var valueTuple in await Task.WhenAll(taskList))
            {
                _output.WriteLine(valueTuple.authenticated+"\n");
                failsCounter += valueTuple.authenticated == false ? 1 : 0;
            }
            Assert.Equal(threads - 1, failsCounter);
        }
        finally
        {
            User? last = await context.Context.users.OrderByDescending(u => u.Id).FirstOrDefaultAsync();
            _output.WriteLine((await context.Context.users.Where(u => u.Id == last!.Id).ExecuteDeleteAsync()).ToString());
        }
    }
    internal AuthService MakeUnmockedService(TestScope scope)
    {
        return new AuthService(new UsersRepository(scope.Context,new XunitLogger<UsersRepository>(_output)),
                                              new CredentialsRepository(scope.Context,new XunitLogger<UsersRepository>(_output)),
                                              new LoginAttemptsRepository(scope.Context),
                                              new SigningKeysRepository(scope.Context,new DataProtectionAPIService(_protector)),new XunitLogger<AuthService>(_output));
    }
}