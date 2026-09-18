using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using Authnomaly.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Authnomaly.Utils;

public sealed class KeyPairRotationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    public KeyPairRotationService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }
    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = CustomTimer.CreateTimer(TimeUnit.DAY, 30);
        do
        {
            using var scope = _scopeFactory.CreateScope();
            var keyStore = scope.ServiceProvider.GetRequiredService<ISigningKeyStore>();

            var newPair = JwtUtils.CreatePair();
            await keyStore.RotateKeyValuePair(newPair.Key, newPair.Value);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}