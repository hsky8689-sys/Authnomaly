using System.Text;
using Authnomaly.Domain;
using Microsoft.IdentityModel.Tokens;

namespace Authnomaly.Utils;
using Konscious.Security.Cryptography;
using System.Security.Cryptography;
public class Encryption
{
    public static (byte[] Hash, byte[] Salt) HashPassword(string password)
    {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
            {
                Salt = salt,
                DegreeOfParallelism = 4,
                Iterations = 3,
                MemorySize = 65536
            };
            byte[] hash = argon2.GetBytes(32);
            return (hash, salt);
    }
    public static bool VerifyPassword(string password, byte[] storedHash, byte[] storedSalt)
    {
        var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = storedSalt,
            DegreeOfParallelism = 4,
            Iterations = 3,
            MemorySize = 65536
        };
        byte[] candidateHash = argon2.GetBytes(32);
        return CryptographicOperations.FixedTimeEquals(candidateHash, storedHash);
    }

    public static void DeleteFromRam(byte[] storedHash, byte[] storedSalt)
    {
        //zerorizes hashed passwords from RAM MEMORY
        CryptographicOperations.ZeroMemory(storedHash);
        CryptographicOperations.ZeroMemory(storedSalt);
    }
}