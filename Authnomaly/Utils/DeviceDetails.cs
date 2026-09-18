using Authnomaly.Domain;
using MaxMind.GeoIP2.Model;

namespace Authnomaly.Utils;
using UAParser;
public class DeviceDetails
{
    //only for logging in our server for now
    public static LoginAttempt CollectAttemptData(HttpContext context)
    {
        var parser = Parser.GetDefault();
        var request = context.Request;
        var connection = context.Connection;
        string userAgentHeader = request.Headers["User-Agent"].ToString();
        ClientInfo clientInfo = parser.Parse(userAgentHeader);
        (string city, string country) = LocationDetection.GetStandardLocation(connection.RemoteIpAddress);
        LoginAttempt attempt = new LoginAttempt(0);
        attempt.IpAddress = connection.RemoteIpAddress;
        attempt.OperatingSystem = $"{clientInfo.OS.Family} {clientInfo.OS.Major}".Trim();
        attempt.Browser = $"{clientInfo.UserAgent.Family} {clientInfo.UserAgent.Major}".Trim();
        attempt.City = city;
        attempt.Country = country;
        attempt.AttemptTime = DateTimeOffset.UtcNow;
        attempt.DeviceName = $"{clientInfo.Device.Brand} {clientInfo.Device.Model}".Trim();
        attempt.Port = connection.RemotePort;
        return attempt;
    }
}