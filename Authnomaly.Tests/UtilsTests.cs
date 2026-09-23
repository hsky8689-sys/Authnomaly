using Authnomaly.Utils;
using Xunit;
using Xunit.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
namespace Authnomaly.Tests;

// Real, publicly-routable IPs (well-known DNS resolvers + documented cloud/CDN ranges).
// Not asserted as precise per-country ground truth (no live source to verify that reliably) -
// meant for smoke-testing the MaxMind lookup path (does it resolve without throwing, does it
// return a non-empty city/country), not for asserting an exact expected country per IP.
static class TestIpAddresses
{
    public static IList<string> GetSampleIps()
    {
        return new List<string>
        {
            "8.8.8.8", "8.8.4.4",           // Google Public DNS
            "1.1.1.1", "1.0.0.1",           // Cloudflare (anycast)
            "9.9.9.9", "149.112.112.112",   // Quad9 (anycast)
            "208.67.222.222", "208.67.220.220", // OpenDNS
            "64.6.64.6", "64.6.65.6",       // Verisign DNS
            "185.228.168.9", "185.228.169.9", // CleanBrowsing
            "76.76.19.19", "76.76.2.0",     // Alternate DNS
            "94.140.14.14", "94.140.15.15", // AdGuard DNS
            "77.88.8.8", "77.88.8.1",       // Yandex DNS (RU)
            "180.76.76.76",                 // Baidu DNS (CN)
            "119.29.29.29",                 // DNSPod (CN)
            "203.80.96.10",                 // HKIX-adjacent block (HK)
            "202.12.27.33",                 // WIDE Project (JP)
            "202.46.34.75",                 // (JP)
            "203.119.4.44",                 // (AU-adjacent APNIC block)
            "1.1.1.2", "1.1.1.3",           // Cloudflare Malware/Adult block variants
            "156.154.70.1", "156.154.71.1", // Neustar/UltraDNS
            "84.200.69.80", "84.200.70.40", // DNS.WATCH (DE)
            "80.80.80.80", "80.80.81.81",   // Freenom World DNS
            "195.46.39.39", "195.46.39.40", // SafeDNS
            "134.195.4.2",                  // (US ARIN block)
            "196.10.99.10",                 // (AFRINIC block)
            "197.210.53.1",                 // (AFRINIC block, NG-adjacent)
            "200.160.2.3",                  // NIC.br (BR)
            "201.6.190.66",                 // (LACNIC block, BR-adjacent)
            "190.98.132.3",                 // (LACNIC block)
            "103.86.96.100",                // (APNIC block)
            "223.5.5.5", "223.6.6.6",       // AliDNS (CN)
            "168.126.63.1", "168.126.63.2", // KT DNS (KR)
            "210.220.163.82",               // (KR-adjacent block)
            "212.23.3.6",                   // (RIPE, EU block)
            "213.133.98.98",                // Hetzner (DE)
            "5.9.164.112",                  // Hetzner (DE)
            "62.210.16.6",                  // OVH (FR)
            "51.15.0.1",                    // Scaleway (FR)
            "129.250.35.250", "129.250.35.251" // NTT (JP/global)
        };
    }
}

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
    [Fact]
    public void LocationDetectionTests()
    {
        ILocationDetector detector = new LocationDetectionGepIp();
        var ips = TestIpAddresses.GetSampleIps();
        int resolved = 0;
        int unknown = 0;
        foreach (var ip in ips)
        {
            (string city, string country) result;
            try
            {
                result = detector.GetStandardLocation(IPAddress.Parse(ip));
            }
            catch (Exception e)
            {
                Assert.Fail($"Lookup threw for {ip}: {e.Message}");
                return;
            }
            // Neither city nor country is guaranteed non-null for every real IP - anycast addresses
            // (e.g. 1.1.1.1) in particular often have no reliable single geographic answer.
            bool hasCountry = !string.IsNullOrEmpty(result.country) && result.country != "Unknown";
            if (hasCountry) resolved++; else unknown++;
            _output.WriteLine($"{ip} -> {result.city ?? "(no city)"}, {result.country ?? "(no country)"}");
        }
        _output.WriteLine($"Resolved: {resolved}/{ips.Count}, Unknown: {unknown}/{ips.Count}");
        // A handful of anycast/no-data addresses is expected and fine - but if the lookup mechanism
        // itself is broken (wrong DB path, corrupt file, etc.), close to *everything* would fail.
        Assert.True(resolved >= ips.Count * 0.7, $"Only {resolved}/{ips.Count} resolved - lookup mechanism may be broken, not just missing data for a few IPs");
    }
    // Pure-math cases derived directly from the formula's own R=6371km sphere assumption
    // (not from an outside real-world/ellipsoid figure, so these are exact, not approximate):
    //   - same point -> 0
    //   - antipodal points -> half the sphere's circumference = pi * R =~ 20015.09 km
    //   - 1 degree of latitude (or of longitude, at the equator) =~ (2*pi*R)/360 =~ 111.195 km
    // City-pair cases use commonly-cited great-circle distances, recalled approximately -
    // tolerances are generous on purpose, these are sanity checks, not precise ground truth.
    [Theory]
    [InlineData(45.0, 25.0, 45.0, 25.0, 0, 0.01)]                    // same point
    [InlineData(90.0, 0.0, -90.0, 0.0, 20015.09, 1)]                 // north pole - south pole (antipodal)
    [InlineData(0.0, 0.0, 0.0, 180.0, 20015.09, 1)]                  // antipodal on the equator
    [InlineData(0.0, 0.0, 0.0, 1.0, 111.195, 0.5)]                   // 1 degree longitude at equator
    [InlineData(45.0, 25.0, 46.0, 25.0, 111.195, 0.5)]                // 1 degree latitude
    [InlineData(45.0, 179.5, 45.0, -179.5, 78.65, 5)]                // crosses the +-180 meridian
    [InlineData(40.7128, -74.0060, 51.5074, -0.1278, 5570, 50)]      // New York - London
    [InlineData(48.8566, 2.3522, 52.5200, 13.4050, 878, 20)]         // Paris - Berlin
    [InlineData(-33.8688, 151.2093, 35.6762, 139.6503, 7800, 100)]   // Sydney - Tokyo
    [InlineData(34.0522, -118.2437, 40.7128, -74.0060, 3940, 50)]    // Los Angeles - New York
    [InlineData(-33.9249, 18.4241, 30.0444, 31.2357, 7290, 100)]     // Cape Town - Cairo
    [InlineData(-22.9068, -43.1729, -34.6037, -58.3816, 1970, 50)]   // Rio de Janeiro - Buenos Aires
    public void ComputeDistanceTests(double lat1, double lon1, double lat2, double lon2, double expectedKm, double tolerance)
    {
        var detector = new LocationDetectionGepIp();
        var actual = detector.ComputeDistance(lat1, lat2, lon1, lon2);
        _output.WriteLine($"({lat1},{lon1}) -> ({lat2},{lon2}): expected ~{expectedKm}km, got {actual:F2}km");
        Assert.InRange(actual, expectedKm - tolerance, expectedKm + tolerance);
    }

    // Ground truth here is NOT an external geolocation API - different providers routinely
    // disagree with MaxMind by hundreds of km for the same IP, so grading MaxMind against another
    // provider's answer isn't really "ground truth" either, just a second opinion.
    // Instead: cross-check GetCoordinates() against GetStandardLocation() for the SAME ip - both
    // read the SAME MaxMind database, so their answers should be internally consistent. Reference
    // points below are capital-city coordinates (a stable, well-known fact, not looked up per-IP),
    // one per country actually observed in LocationDetectionTests's real output. Tolerance is per
    // country and deliberately generous (large countries span thousands of km) - this test catches
    // gross inconsistencies (e.g. swapped lat/lon, wrong hemisphere), not precision.
    private static readonly Dictionary<string, (double lat, double lon, double toleranceKm)> CountryReferencePoints = new()
    {
        ["United States"] = (38.9072, -77.0369, 2500),
        ["Canada"] = (45.4215, -75.6972, 3000),
        ["Cyprus"] = (35.1856, 33.3823, 150),
        ["Russia"] = (55.7558, 37.6173, 5500),
        ["China"] = (39.9042, 116.4074, 2800),
        ["Hong Kong"] = (22.3193, 114.1694, 100),
        ["Japan"] = (35.6762, 139.6503, 1200),
        ["Philippines"] = (14.5995, 120.9842, 800),
        ["Germany"] = (52.5200, 13.4050, 500),
        ["The Netherlands"] = (52.3676, 4.9041, 200),
        ["South Africa"] = (25.7479, 28.2293, 1200),
        ["Nigeria"] = (9.0765, 7.3986, 800),
        ["Brazil"] = (-15.7801, -47.9292, 3000),
        ["Argentina"] = (-34.6037, -58.3816, 1800),
        ["Australia"] = (-35.2809, 149.1300, 3000),
        ["South Korea"] = (37.5665, 126.9780, 250),
        ["United Kingdom"] = (51.5074, -0.1278, 400),
        ["France"] = (48.8566, 2.3522, 500),
    };
    [Fact]
    public void GetCoordinatesConsistencyTests()
    {
        ILocationDetector detector = new LocationDetectionGepIp();
        var ips = TestIpAddresses.GetSampleIps();
        int checkedCount = 0;
        int consistent = 0;
        foreach (var ip in ips)
        {
            var address = IPAddress.Parse(ip);
            (string city, string country) location;
            try
            {
                location = detector.GetStandardLocation(address);
            }
            catch (Exception e)
            {
                _output.WriteLine($"{ip}: GetStandardLocation threw ({e.Message}), skipping");
                continue;
            }
            if (string.IsNullOrEmpty(location.country) || !CountryReferencePoints.TryGetValue(location.country, out var reference))
            {
                _output.WriteLine($"{ip}: no reference point for country '{location.country}', skipping");
                continue;
            }

            (double? Latitude, double? Longitude) coords;
            try
            {
                coords = detector.GetCoordinates(address);
            }
            catch (Exception e)
            {
                _output.WriteLine($"{ip}: GetCoordinates threw ({e.Message}), skipping");
                continue;
            }
            if (coords.Latitude is null || coords.Longitude is null)
            {
                _output.WriteLine($"{ip}: GetCoordinates returned null lat/lon, skipping");
                continue;
            }

            checkedCount++;
            double distance = detector.ComputeDistance(coords.Latitude.Value, reference.lat, coords.Longitude.Value, reference.lon);
            bool ok = distance <= reference.toleranceKm;
            if (ok) consistent++;
            _output.WriteLine($"{ip} ({location.country}): coords=({coords.Latitude},{coords.Longitude}), distance to reference={distance:F0}km, tolerance={reference.toleranceKm}km, {(ok ? "OK" : "MISMATCH")}");
        }
        _output.WriteLine($"Consistent: {consistent}/{checkedCount}");
        Assert.True(checkedCount > 0, "No IPs had a usable country + coordinates pair - can't validate consistency");
        Assert.True(consistent >= checkedCount * 0.8, $"Only {consistent}/{checkedCount} were geographically consistent - GetCoordinates and GetStandardLocation may disagree, or lat/lon may be swapped somewhere");
    }

    // Builder pattern: sensible defaults (a token JwtUtils.ValidateJwt would accept), override only
    // what a given test needs bad on purpose (issuer, audience, expiry, or the signing key itself).
    private static string BuildJwt(SecurityKey signingKey,
                                   string username = "testuser",
                                   Guid? familyId = null,
                                   string issuer = "http://localhost:5000",
                                   string audience = "http://localhost:5000",
                                   DateTime? expires = null,
                                   DateTime? issuedAt = null)
    {
        var handler = new JsonWebTokenHandler();
        var subject = new ClaimsIdentity(new[]
        {
            new Claim("username", username),
            new Claim("roles", "user"),
            new Claim("jti", Guid.NewGuid().ToString()),
            new Claim("familyId", (familyId ?? Guid.NewGuid()).ToString()),
            new Claim("trust", "100")
        });
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = subject,
            Issuer = issuer,
            Audience = audience,
            Expires = expires ?? DateTime.UtcNow.AddMinutes(30),
            IssuedAt = issuedAt ?? DateTime.UtcNow,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)
        };
        return handler.CreateToken(tokenDescriptor);
    }

    [Fact]
    public void JwtBasicTests()
    {
        var pair = JwtUtils.CreatePair();
        var publicKey = pair.Key;
        var privateKey = pair.Value;

        Assert.False(string.IsNullOrEmpty(privateKey.KeyId));
        Assert.Equal(privateKey.KeyId, publicKey.KeyId);
        Assert.NotNull(privateKey.Parameters.D); // private exponent - only present on the private key
        Assert.Null(publicKey.Parameters.D);
    }

    [Fact]
    public async Task CreateJwt_ThenValidateJwt_WithGoodData_ReturnsTrue()
    {
        var pair = JwtUtils.CreatePair();
        var jwt = JwtUtils.CreateJwt("testuser", Guid.NewGuid(), pair.Value);

        bool valid = await JwtUtils.ValidateJwt(jwt, pair.Key);

        Assert.True(valid);
    }

    [Fact]
    public async Task ValidateJwt_SignedByDifferentKeyPair_ReturnsFalse()
    {
        var correctPair = JwtUtils.CreatePair();
        var wrongPair = JwtUtils.CreatePair();
        var jwt = BuildJwt(correctPair.Value);

        bool valid = await JwtUtils.ValidateJwt(jwt, wrongPair.Key);

        Assert.False(valid);
    }

    [Fact]
    public async Task ValidateJwt_ExpiredToken_ReturnsFalse()
    {
        var pair = JwtUtils.CreatePair();
        var jwt = BuildJwt(pair.Value,
            issuedAt: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1));

        bool valid = await JwtUtils.ValidateJwt(jwt, pair.Key);

        Assert.False(valid);
    }

    [Fact]
    public async Task ValidateJwt_WrongIssuer_ReturnsFalse()
    {
        var pair = JwtUtils.CreatePair();
        var jwt = BuildJwt(pair.Value, issuer: "http://evil.example.com");

        bool valid = await JwtUtils.ValidateJwt(jwt, pair.Key);

        Assert.False(valid);
    }

    [Fact]
    public async Task ValidateJwt_WrongAudience_ReturnsFalse()
    {
        var pair = JwtUtils.CreatePair();
        var jwt = BuildJwt(pair.Value, audience: "http://someone-else.example.com");

        bool valid = await JwtUtils.ValidateJwt(jwt, pair.Key);

        Assert.False(valid);
    }

    [Fact]
    public async Task ValidateJwt_TamperedSignature_ReturnsFalse()
    {
        var pair = JwtUtils.CreatePair();
        var jwt = JwtUtils.CreateJwt("testuser", Guid.NewGuid(), pair.Value);
        // flip the last character of the signature segment - still well-formed (3 dot-separated
        // parts), but the signature no longer matches the header+payload
        char lastChar = jwt[jwt.Length - 1];
        char replacement = lastChar == 'A' ? 'B' : 'A';
        var tampered = jwt.Substring(0, jwt.Length - 1) + replacement;

        bool valid = await JwtUtils.ValidateJwt(tampered, pair.Key);

        Assert.False(valid);
    }

    [Fact]
    public async Task ValidateJwt_GarbageString_ReturnsFalse()
    {
        var pair = JwtUtils.CreatePair();

        bool valid = await JwtUtils.ValidateJwt("not.a.jwt", pair.Key);

        Assert.False(valid);
    }
}