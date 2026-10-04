using Authnomaly.Domain;
using Authnomaly.Repositories.DatabaseRepositories;
using Authnomaly.Repositories.Interfaces;
using Authnomaly.Repositories.MemoryRepositories;
using Authnomaly.Services;
using Authnomaly.Utils;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.IdentityModel.Tokens;
using Moq;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace Authnomaly.Tests;

public class JwtServiceTests
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<ISigningKeyStore> _keyStore;
    private IList<SigningKey> _mockSigningKeys;
    private readonly Mock<ITokenBlacklistStore> _tokenStore;
    private readonly ISigningKeyProtection _protection;
    private readonly IDataProtector _protector;
    private readonly JwtService _service;
    private readonly TestDb _db = new(); 
    public JwtServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _mockSigningKeys = new List<SigningKey>();
        _keyStore = new Mock<ISigningKeyStore>();
        _protector = DataProtectionProvider.Create("test-app").CreateProtector("Authnomaly.SigningKeys");
        _protection = new DataProtectionAPIService(_protector);
        _tokenStore = new Mock<ITokenBlacklistStore>();
        _keyStore.Setup(u => u.CleanupExpiredKeys()).Returns(() =>
        {
            foreach (var key in _mockSigningKeys)
            {
                if (DateTimeOffset.Compare(key.RetiredAt.Value, DateTimeOffset.UtcNow) < 0)
                    key.IsCurrent = false;
            }
            return Task.FromResult(_mockSigningKeys);
        });
        _keyStore.Setup(u => u.GetCurrentActiveKeysAsync()).ReturnsAsync(() =>
        {
            return _mockSigningKeys.Where(k => k.IsCurrent).ToList();
        });
        _keyStore.Setup(u => u.GetLastActivePair()).ReturnsAsync(() =>
        {
            return _mockSigningKeys.Where(u=>u.IsCurrent)
                                    .OrderByDescending(u => u.CreatedAt)
                                   .FirstOrDefault();
        });
        _keyStore.Setup(u => u.RotateKeyValuePair(It.IsAny<RsaSecurityKey>(),
                                                       It.IsAny<RsaSecurityKey>()))
                                                                .Returns((RsaSecurityKey publicKey,RsaSecurityKey privateKey) =>
        {
            //mock only does addition here,despite the repo invalidating whatever was still inactive
            var before = _mockSigningKeys.Count;
            var publicPem = JwtUtils.ExportPublicKeyPem(publicKey);
            var privatePem = JwtUtils.ExportPrivateKeyPem(privateKey);
            var encryptedPrivate = _protection.Encrypt(privatePem);
            SigningKey key = new SigningKey(0, publicKey.KeyId, publicPem, encryptedPrivate); 
            _mockSigningKeys.Add(key);
            return Task.FromResult(_mockSigningKeys.Count!=before);
        });
        _service = new JwtService(_keyStore.Object,_tokenStore.Object);
        for (int i = 0; i < 100; i++)
        {
            var pair = JwtUtils.CreatePair();
            _keyStore.Object.RotateKeyValuePair(pair.Key, pair.Value);
        }
    }
    [Fact]
    public async Task GetLastPrivateKeyTest()
    {
        using var mplexer = makeMultiplexer();
        var service = MakeUnmockedService(_db.NewScope(), mplexer);
        var last = await service.GetLastPrivateKey();
        Assert.True(last.IsCurrent);
        string lastKid = last.Kid;
        var keysRepository = new SigningKeysRepository(_db.NewScope().Context, new DataProtectionAPIService(_protector));
        var newPair = JwtUtils.CreatePair();
        await keysRepository.RotateKeyValuePair(newPair.Key,newPair.Value);
        var newLast = await service.GetLastPrivateKey();
        Assert.NotEqual(lastKid,newLast.Kid);
    }
    internal IConnectionMultiplexer makeMultiplexer()
    {
        return ConnectionMultiplexer.Connect("localhost");
    }
    internal JwtService MakeUnmockedService(TestScope scope,IConnectionMultiplexer multiplexer)
    {
        RedisCacheOptions options = new RedisCacheOptions();
        options.ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer);
        IDistributedCache cache = new RedisCache(options);
        return new JwtService(new SigningKeysRepository(scope.Context,new DataProtectionAPIService(_protector)),
                                new TokenBlacklistRepository(cache));
    }
}