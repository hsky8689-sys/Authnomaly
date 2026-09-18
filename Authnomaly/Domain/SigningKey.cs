using System.ComponentModel.DataAnnotations.Schema;
using Authnomaly.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Authnomaly.Domain;

[Table("SigningKeys")]
[PrimaryKey("Id")]
public class SigningKey : Entity<long>
{
    private string _kid = default!;
    private string _encryptedPrivateKey = default!;
    private string _publicKey = default!;
    private DateTimeOffset _createdAt = default!;
    private DateTimeOffset? _retiredAt = default;
    private bool _isCurrent = default!;
    public string Kid
    {
        get => _kid;
        set => _kid = value ?? throw new ArgumentNullException(nameof(value));
    }
    public string EncryptedPrivateKey
    {
        get => _encryptedPrivateKey;
        set => _encryptedPrivateKey = value ?? throw new ArgumentNullException(nameof(value));
    }
    public string PublicKey
    {
        get => _publicKey;
        set => _publicKey = value ?? throw new ArgumentNullException(nameof(value));
    }
    public DateTimeOffset CreatedAt
    {
        get => _createdAt;
        set => _createdAt = value;
    }
    public DateTimeOffset? RetiredAt
    {
        get => _retiredAt;
        set => _retiredAt = value;
    }
    public bool IsCurrent
    {
        get => _isCurrent;
        set => _isCurrent = value;
    }
    public SigningKey(long id) : base(id)
    {
        
    }
    public SigningKey(long id, string kid, string publicKeyPem, string encryptedPrivateKeyPem) : base(id)
    {
        Kid = kid;
        PublicKey = publicKeyPem;
        EncryptedPrivateKey = encryptedPrivateKeyPem;
        CreatedAt = DateTimeOffset.UtcNow;
        RetiredAt = null;
        IsCurrent = true;
    }
}