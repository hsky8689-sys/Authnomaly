using Authnomaly.Repositories.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Authnomaly.Utils;

public class CleanupSecurityKeysService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    public CleanupSecurityKeysService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }
    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = CustomTimer.CreateTimer(TimeUnit.DAY, 1);
        do
        {
            using var scope = _scopeFactory.CreateScope();
            var keyStore = scope.ServiceProvider.GetRequiredService<ISigningKeyStore>();
            await keyStore.CleanupExpiredKeys();
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}