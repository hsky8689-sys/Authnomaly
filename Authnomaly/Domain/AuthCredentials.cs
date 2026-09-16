namespace Authnomaly.Domain;

public class AuthCredentials : Entity<long>
{
    private string _username = default!;
    private string _password = default!;
    public string Username
    {
        get => _username;
        set => _username = value ?? throw new ArgumentNullException(nameof(value));
    }
    public string Password
    {
        get => _password;
        set => _password = value ?? throw new ArgumentNullException(nameof(value));
    }
    public AuthCredentials(long id) : base(id)
    {
        
    }
    public AuthCredentials(long id, string username, string password) : base(id)
    {
        Username = username;
        password = password;
    }
}