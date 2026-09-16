namespace Authnomaly.Domain;

/*
 * Entity used for mapping data about client applications using the authentication server
 * By default our app will be stored with the Id 1
*/
public class RegisteredClientApplication : Entity<long>
{
    private string _successfulLoginRedirectUrl = default!;
    private string _usernameRegex = default!;
    private string _passwordRegex = default!;
    public RegisteredClientApplication(long id) : base(id)
    {
        
    }
}