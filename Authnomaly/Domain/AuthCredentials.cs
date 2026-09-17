using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Authnomaly.Domain;
[Table("AuthCredentials")]
[PrimaryKey("Id")]
public class AuthCredentials : Entity<long>
{
    private string _username = default!;
    private string _password = default!;
    private User _owner = default!;
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
    public User Owner
    {
        get => _owner;
        set => _owner = value ?? throw new ArgumentNullException(nameof(value));
    }
    public AuthCredentials(long id) : base(id)
    {
        
    }
    public AuthCredentials(long id, string username, string password,User owner) : base(id)
    {
        Username = username;
        Password = password;
        Owner = owner;
    }
}