using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Authnomaly.Domain;
[Table("AuthCredentials")]
[PrimaryKey("Id")]
public class AuthCredentials : Entity<long>
{
    private string _username = default!;
    private string _passwordHash = default!;
    private User _owner = default!;
    private byte[] _salt = default!;
    public string Username
    {
        get => _username;
        set => _username = value ?? throw new ArgumentNullException(nameof(value));
    }
    public string PasswordHash
    {
        get => _passwordHash;
        set => _passwordHash = value ?? throw new ArgumentNullException(nameof(value));
    }
    public User Owner
    {
        get => _owner;
        set => _owner = value ?? throw new ArgumentNullException(nameof(value));
    }

    public byte[] Salt
    {
        get => _salt;
        set => _salt = value;
    }
    public AuthCredentials(long id) : base(id)
    {
        
    }
    public AuthCredentials(long id, string username, string password,User owner,byte[] salt) : base(id)
    {
        Username = username;
        PasswordHash = password;
        Owner = owner;
        Salt = salt;
    }
}