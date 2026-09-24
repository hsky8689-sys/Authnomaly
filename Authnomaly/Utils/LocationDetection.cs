using System.Net;
using MaxMind.GeoIP2;

namespace Authnomaly.Utils;

public interface ILocationDetector
{
    (string city, string country) GetStandardLocation(IPAddress ipAddress);
    double ComputeDistance(double lat1, double lat2, double lon1, double lon2);
    (double? Latitude, double? Longitude) GetCoordinates(IPAddress ipAddress);
}

public sealed class LocationDetectionGepIp : ILocationDetector
{
    private static readonly string _pathToGeo = Environment.GetEnvironmentVariable("GEO_LOCALDB_PATH");
    private static DatabaseReader? _reader = null;
    private static DatabaseReader GetReader()
    {
        if (_reader is null) _reader = new DatabaseReader(_pathToGeo);
        return _reader;
    }

    public (string city, string country) GetStandardLocation(IPAddress ipAddress)
    {
        try
        {
            var reader = GetReader();
            var city = reader.City(ipAddress);
            return (city.City.Name, city.Country.Name);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            // private/loopback/reserved addresses (e.g. ::1, 127.0.0.1) are never in the database
            return ("Unknown", "Unknown");
        }
    }
    public (double? Latitude,double? Longitude) GetCoordinates(IPAddress ipAddress)
    {
        var reader = GetReader();
        var city = reader.City(ipAddress);
        return (city.Location.Latitude, city.Location.Longitude);
    }
    //Harvesine mathematical formula for distance between 2 points on Earth
    public double ComputeDistance(double lat1,double lat2,double lon1,double lon2)
    {
        const double R = 6371;
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat/2)*Math.Sin(dLat/2)+
                       Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * 
                       Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }
}