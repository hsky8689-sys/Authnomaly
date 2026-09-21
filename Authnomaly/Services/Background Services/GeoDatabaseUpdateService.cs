namespace Authnomaly.Utils;
using System.Net.Http;

public enum TimeUnit
{
    MILISECOND,
    SECOND,
    MINUTE,
    HOUR,
    DAY
}
public sealed class CustomTimer
{
    public static PeriodicTimer CreateTimer(TimeUnit unit,long amount)
    {
        return unit switch
        {
            TimeUnit.MILISECOND => new PeriodicTimer(TimeSpan.FromMilliseconds(amount)),
            TimeUnit.SECOND => new PeriodicTimer(TimeSpan.FromSeconds(amount)),
            TimeUnit.MINUTE => new PeriodicTimer(TimeSpan.FromMinutes(amount)),
            TimeUnit.HOUR => new PeriodicTimer(TimeSpan.FromHours(amount)),
            TimeUnit.DAY => new PeriodicTimer(TimeSpan.FromDays(amount)),
            _ => new PeriodicTimer(TimeSpan.FromSeconds(amount))
        };
    }
}
public sealed class GeoDatabaseUpdateService : BackgroundService
{
    private int _daysToWait;
    private PeriodicTimer _timer;
    public GeoDatabaseUpdateService(PeriodicTimer timer)
    {
        _daysToWait = 30;
        _timer = timer;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        do
        {
            await UpdateDatabase();
        } while (await _timer.WaitForNextTickAsync(stoppingToken));
    }
    private async Task<HttpResponseMessage> UpdateDatabase()
    {
        try
        {
            var license_key = Environment.GetEnvironmentVariable("GEO_LICENSE_KEY");
            var account_id = Environment.GetEnvironmentVariable("GEO_ACCOUNT_ID");
            string curl =
                $"curl -I -L -u {account_id}:{license_key} https://download.maxmind.com/geoip/databases/GeoIP2-City-CSV/download?suffix=zip";
            using HttpClient client = new HttpClient();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, curl);
            request.Headers.Add("Authorization",
                $"Basic {Convert.ToBase64String(System.Text.ASCIIEncoding.ASCII.GetBytes($"{account_id}:{license_key}"))}");
            HttpResponseMessage response = await client.SendAsync(request);
            return response;
        }
        catch(Exception e)
        {
            return new HttpResponseMessage();
        }
    }
}