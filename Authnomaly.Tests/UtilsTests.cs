using Authnomaly.Utils;
using Xunit;
using Xunit.Abstractions;
using System.Security.Cryptography;
using System.Text;
namespace Authnomaly.Tests; 
class PasswordGenerator
{
    private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
                                 "abcdefghijklmnopqrstuvwxyz" +
                                 "0123456789" +
                                 "!@#$%^&*()-_=+[]{}|;:,.<>?";

    public static string Generate(int length = 16)
    {
        var password = new StringBuilder(length);
        byte[] randomBytes = new byte[length * 4];
        
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
            
            for (int i = 0; i < length; i++)
            {
                uint randomValue = BitConverter.ToUInt32(randomBytes, i * 4);
                password.Append(Chars[(int)(randomValue % Chars.Length)]);
            }
        }
        return password.ToString();
    }
}
public class UtilsTests
{
    private readonly ITestOutputHelper _output;

    public UtilsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private IList<string> GenerateRandomPasswords(int maxLength = 40,
                                                 int count = 35)
    {
        IList<string> passwords = new List<string>();
        for (int i = 0; i < count; i++)
        {
            int currentLength = RandomNumberGenerator.GetInt32(maxLength);
            currentLength = Math.Max(currentLength, 5);//argon can't hash ""
            var newString = PasswordGenerator.Generate(currentLength);
            passwords.Add(newString);
        }
        return passwords;
    }
    
    // robust to outliers - unaffected by a handful of very large/small values, unlike a mean
    private static long Median(List<long> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2
            : sorted[mid];
    }
    
    //thread creation form multi thread encryption memory leak tests
    private static Thread CreateThread(int batchSize,
                                       int index,
                                       IList<string> passwordsBatch,
                                       IList<long> bytesPerBatch)
    {
        return new Thread(() =>
            {
                int firstPassIndex = index * batchSize;
                int lastPassIndex = Math.Min(passwordsBatch.Count, firstPassIndex+batchSize);
                for (int j = firstPassIndex; j < lastPassIndex; j++)
                {
                    long alloced = GC.GetAllocatedBytesForCurrentThread();
                    var pass = Encryption.HashPassword(passwordsBatch[j]);
                    Encryption.DeleteFromRam(pass.Hash, pass.Salt);
                    bytesPerBatch[index] += GC.GetAllocatedBytesForCurrentThread()-alloced;
                }
            });
    }
    [Theory]
    [InlineData(50,100,10,3)]
    public void EncryptionTests(int maxLength,int count,int batchSize,float tolerance)
    {
        //tests for obvious cases
        string pass1 = "dccaweqwcsar3";
        var encrypted1 = Encryption.HashPassword(pass1);
        Assert.True(Encryption.VerifyPassword(pass1, encrypted1.Hash, encrypted1.Salt));
        string pass2 = "-eg23s<a;ew32p0o@[[]";
        var encrypted2 = Encryption.HashPassword(pass2);
        Assert.False(Encryption.VerifyPassword(pass2, encrypted1.Hash, encrypted1.Salt));
        Assert.False(Encryption.VerifyPassword(pass2, encrypted1.Hash, encrypted2.Salt));
        Assert.False(Encryption.VerifyPassword(pass1, [], []));
        Assert.False(Encryption.VerifyPassword(pass2, [], []));
        Encryption.DeleteFromRam(encrypted1.Hash,encrypted1.Salt);
        Encryption.DeleteFromRam(encrypted2.Hash,encrypted2.Salt);
        GC.Collect();
        _output.WriteLine("Checkpoint 0:obvious tests passed");

        //tests for memory allocation over the same thread
        var batchMedians = new List<long>();
        var currentBatchValues = new List<long>();
        var passwordsBatch1 = GenerateRandomPasswords(maxLength, count);
        foreach (var password in passwordsBatch1)
        {
            long alloced = GC.GetTotalMemory(true);
            var pass = Encryption.HashPassword(password);
            Encryption.DeleteFromRam(pass.Hash, pass.Salt);
            long allocedAfterHash = GC.GetTotalMemory(true);
            
            currentBatchValues.Add(allocedAfterHash - alloced);

            if (currentBatchValues.Count == batchSize)
            {
                batchMedians.Add(Median(currentBatchValues));
                currentBatchValues.Clear();
                GC.Collect();
            }
        }
        if (currentBatchValues.Count > 0) // flush the last, partial batch
        {
            batchMedians.Add(Median(currentBatchValues));
        }

        
        int half = batchMedians.Count / 2;
        double firstHalfAvg = batchMedians.Take(half).Average();
        double secondHalfAvg = batchMedians.Skip(half).Average();

        _output.WriteLine($"Batches: {batchMedians.Count}");
        _output.WriteLine($"First half avg of batch medians: {firstHalfAvg} bytes");
        _output.WriteLine($"Second half avg of batch medians: {secondHalfAvg} bytes");
        Assert.True(Math.Abs(secondHalfAvg) <= Math.Abs(firstHalfAvg * tolerance));
        
        GC.Collect();
        _output.WriteLine("Checkpoint 1:single threaded tests passed");
        //tests for memory allocation over multiple threads
        var passwordsBatch2 = GenerateRandomPasswords(maxLength, count);
        List<Thread> threads = new List<Thread>();
        List<long> bytesPerBatch = new List<long>();
        
        for (int i = 0; i < batchSize; i++)
        {
            bytesPerBatch.Add(0);
            threads.Add(CreateThread(batchSize,i,passwordsBatch2,bytesPerBatch));
        }
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        // NOTE: bytesPerBatch here is one aggregate value per thread already (not raw per-hash
        // values), so there's no inner batch to take a median of - just compare first/second half directly.
        int threadHalf = bytesPerBatch.Count / 2;
        double firstHalfAvgMt = bytesPerBatch.Take(threadHalf).Average();
        double secondHalfAvgMt = bytesPerBatch.Skip(threadHalf).Average();
        
        _output.WriteLine($"Threads: {bytesPerBatch.Count}");
        _output.WriteLine($"First half avg bytes per thread: {firstHalfAvgMt} bytes");
        _output.WriteLine($"Second half avg bytes per thread: {secondHalfAvgMt} bytes");
        Assert.True(Math.Abs(secondHalfAvgMt) <= Math.Abs(firstHalfAvgMt * tolerance));
        GC.Collect();
        _output.WriteLine("Checkpoint 2:multi threaded tests passed");
    }
    [Theory]
    [InlineData(12,20)]
    public void RamZerorizationTests(int length,int count)
    {
        byte[][] randomGenerated = new byte[length][];
        for (int i = 0; i < length; i++) randomGenerated[i] = RandomNumberGenerator.GetBytes(count);
        for (int i = 0; i < length; i++)
        {
            var hashed = Encryption.HashPassword(randomGenerated[i].ToString() ?? string.Empty);
            Encryption.DeleteFromRam(hashed.Hash, hashed.Salt);
            Assert.All(hashed.Hash, b => Assert.Equal(0, b));
            Assert.All(hashed.Salt, b => Assert.Equal(0, b));
        }
        GC.Collect();
    }
    [Theory]
    [InlineData(10, 14)]
    public void MultipleHashingTests(int times,int passwordLength)
    {
        string password = PasswordGenerator.Generate(length: passwordLength);
        ISet<byte[]> salts = new HashSet<byte[]>();
        var firstHash =  Encryption.HashPassword(password).Salt ??  new byte[] { };
        if (firstHash.Length == 0) Assert.Fail("Could not hash initial password");
        salts.Add(firstHash);
        for (int i = 0; i < times; i++)
        {
            var hashedData = Encryption.HashPassword(password);
            var salt = hashedData.Salt ?? new byte[] { };
            Assert.True(salts.Add(salt));
        }
        GC.Collect();
    }
}