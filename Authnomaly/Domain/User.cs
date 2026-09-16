using System.ComponentModel.DataAnnotations;

namespace Authnomaly.Domain;

public class User : Entity<long>
{
    private string _username = default!;
    private string _email = default!;
    [MaxLength(255)]
    public string Username
    {
        get => _username;
        set => _username = value ?? throw new ArgumentNullException(nameof(value));
    }
    [MaxLength(255)]
    public string Email
    {
        get => _email;
        set => _email = value ?? throw new ArgumentNullException(nameof(value));
    }
    public User(long id) : base(id)
    {
        
    }
    public User(long id, string username, string email) : base(id)
    {
        Username = username;
        Email = email;
    }
}