using Authnomaly.Controllers;
using Authnomaly.Domain;

namespace Authnomaly.Utils;

public class RateLimitUtils
{
    /*
     * rl:login:IP
     * rl:login:USERNAME
     * http://localhost:5000 /api/Clients/lo0gin 
     * http://localhost:5000 /api/Clients/lo0gin
     */
    public static IList<string> GetMatch(EndpointLimitData data)
    {
        const string appUrl = "http://localhost:5000/";
        var url = data.Path;
        var lastIndex = url.IndexOf(appUrl);
        if (lastIndex == -1) return new List<string>(){""};
        var splitted = url.Substring(lastIndex+appUrl.Length).Split("/");
        if(splitted.Length < 3 || !splitted[0].Equals("api"))
                    return new List<string>(){""};
        var endpoint = splitted[2];
        return new List<string>(){$"rl:{endpoint}:{data.Adress}"};
    }
}