using Authnomaly.Services;

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
            var jwtService = scope.ServiceProvider.GetRequiredService<JwtService>();
            await jwtService.RotateSigningKey();
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}