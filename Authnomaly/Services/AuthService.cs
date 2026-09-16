using Authnomaly.Repositories.DatabaseRepositories;
using Authnomaly.Repositories.Interfaces;

namespace Authnomaly.Services;

public class AuthService
{
    private readonly IUsersRepo _usersRepo;
    public AuthService(IUsersRepo usersRepo)
    {
        _usersRepo = usersRepo;
    }
    public void authenticate()
    {
        
    }

    public void login()
    {
        
    }
}