using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Authnomaly.Domain;
using Authnomaly.Repositories.DatabaseRepositories;
using Xunit;
using Xunit.Abstractions;

namespace Authnomaly.Tests;

public class RateLimitingTests
{
    private ITestOutputHelper _output;
    private TestDb _db;

    public RateLimitingTests(ITestOutputHelper output)
    {
        _output = output;
        _db = new TestDb();
    }

    public enum InAppEndpoint
    {
        InAppLogin,
        InAppRegister,
        InAppRefreshToken,
        /*
         * statistics per app
         * add app
         */
    }
    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        MaxConnectionsPerServer = 256
    }) { BaseAddress = new Uri("http://localhost:5000") };
    private IReadOnlyDictionary<InAppEndpoint, string> knownEndpoints =
        new Dictionary<InAppEndpoint, string>()
        {
            [InAppEndpoint.InAppLogin] = "/api/Clients/login",
            [InAppEndpoint.InAppRegister] = "/api/Clients/register",
            [InAppEndpoint.InAppRefreshToken] = "api/Clients/refresh"
        };
    private async Task<HttpResponseMessage> SendAndIntercept(HttpClient client,
        HttpMethod method,
        string url,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body != null)
        {
            string json = JsonSerializer.Serialize(body);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
    }
    private Dictionary<string, object?> CreateDefaultBodyDictionary(InAppEndpoint endpoint)
    {
        return endpoint switch
        {
            InAppEndpoint.InAppLogin => new() { ["Username"] = "nu conteaza", ["Password"] = "nici asta" },
            InAppEndpoint.InAppRegister => new() { ["Username"] = "nu conteaza", ["Password"] = "nici asta" },
            InAppEndpoint.InAppRefreshToken => new(), //TODO
            /*DE ASEMENEA TO DO PT RESTUL DE ENDPOINTS*/
            _ => new()
        };
    }
    private static (int Min, int Max) ExpectedAcceptedForMultipleRules(int[] ruleIndexes,
        int requestsSent,
        int[] limits,
        int[] windows,
        RateLimitStrategy[] strategies,
        double runTimeSeconds)
    {
        /*
         *
         */
        int min = ruleIndexes.Min(i => limits[i]);
        double max = ruleIndexes.Min(i =>
            limits[i] + (strategies[i] == RateLimitStrategy.Bucket ? runTimeSeconds * limits[i] / windows[i] : 0));
        return (Math.Min(min, requestsSent), Math.Min((int)Math.Ceiling(max), requestsSent));
    }

    private bool CheckResultsOutput(RateLimitStrategy strategy, double runTimeSeconds, int limit, int window,
        int accepted, double allowedError = 10e7)
    {
        double total = (runTimeSeconds / window) * limit;
        if (runTimeSeconds % window > 0) total += limit;
        return (accepted - allowedError <= total && accepted + allowedError >= total)
               ||
               (strategy.Equals(RateLimitStrategy.FixedWindow) &&
                accepted - allowedError <= 2 * total && accepted + allowedError >= total * 2); //fixed window edge case
    }

    [Theory]
    [InlineData(500, 60, 100, "POST", 0, null, 0, 2)]
    [InlineData(500, 60, 100, "POST", 0, null, 0, 0)] //static window on login
    [InlineData(500, 120, 300, "POST", 0, null, 0, 2)]
    [InlineData(300, 60, 500, "POST", 0, null, 1, 1)]
    [InlineData(400, 10, 500, "POST", 0, null, 0, 3)]
    [InlineData(200, 10, 500, "POST", 0, null, 1, 0)]
    [InlineData(600, 45, 15, "POST", 0, null, 1, 2)]
    [InlineData(200, 60, 500, "POST", 0, null, 1, 1)]
    [InlineData(500, 10, 500, "POST", 0, null, 0, 1)]
    [InlineData(400, 10, 500, "POST", 0, null, 1, 0)]
    [InlineData(350, 120, 300, "POST", 0, null, 1, 0)]
    [InlineData(500, 60, 500, "POST", 0, null, 1, 1)]
    [InlineData(300, 10, 500, "POST", 0, null, 0, 3)]
    [InlineData(270, 10, 500, "POST", 0, null, 1, 0)]
    [InlineData(300, 60, 500, "POST", 0, null, 0, 3)]
    [InlineData(200, 60, 500, "POST", 0, null, 1, 3)]
    [InlineData(500, 10, 500, "POST", 0, null, 0, 3)]
    [InlineData(400, 10, 500, "POST", 0, null, 1, 2)]
    public async Task BasicSpamTests(int multiple, int window, int limit, string method, int source, string? fieldName,
        int appEndpoint, int strategy)
    {
        /*
         usage multiple-how many spam tries
                    -window
                    -limit
                    -http method
                    -int value of source attribute for rate limitting
                    -field name or NULL
                    -int value of appEndpoint attribute
                    -int value of RateLimitStrategy attribute
         used for checking rate limitters resistance to basic spam againist in-app endpoints
         basic tests for only 1 rule per endpoint by now
         Rate limit strategy values 0-2
         InAppendpoint 0-2(by now)
         HttpMethod parse via string
        */
        HttpMethod parsedMethod = HttpMethod.Post;
        InAppEndpoint parsedEndpoint = InAppEndpoint.InAppLogin;
        RateLimitStrategy parsedStrategy = RateLimitStrategy.FixedWindow;
        RateLimitSource parsedSource = RateLimitSource.Ip;
        try
        {
            parsedMethod = HttpMethod.Parse(method);
            parsedEndpoint = (InAppEndpoint)appEndpoint;
            parsedStrategy = (RateLimitStrategy)strategy;
            parsedSource = (RateLimitSource)source;
            if (parsedSource.Equals(RateLimitSource.Ip) && fieldName is not null)
            {
                _output.WriteLine("[WARNING] IP-based rate limitting does not use any other filter");
                fieldName = null;
            }
        }
        catch (Exception e)
        {
            Assert.Fail($"Exception {e.Message} while parsing arguments");
        }

        var repo = new EndpointLimitDataRepository(_db.NewScope().Context, _db.Multiplexer);
        long id = -1;
        try
        {
            try
            {
                id = await repo.Add(new EndpointLimitData(0, parsedMethod,
                    knownEndpoints[parsedEndpoint], 1,
                    parsedSource, window, limit, fieldName,
                    parsedStrategy));
            }
            catch
            {
                await repo.Delete(id);
            }

            Assert.NotEqual(0, id);
            var task = async () =>
            {
                HttpClient client = new HttpClient(new SocketsHttpHandler
                {
                    MaxConnectionsPerServer = 256
                }) { BaseAddress = new Uri("http://localhost:5000") };
                var response = await SendAndIntercept(
                    client,
                    parsedMethod,
                    knownEndpoints[parsedEndpoint],
                    CreateDefaultBodyDictionary(parsedEndpoint)
                );
                return response.StatusCode;
            };
            var clientList = Enumerable.Repeat(task, multiple).Select(task => { return task.Invoke(); });
            int accepted = 0;
            var sw = Stopwatch.StartNew();
            foreach (var result in await Task.WhenAll(clientList))
            {
                accepted += result.CompareTo(HttpStatusCode.TooManyRequests) != 0 ? 1 : 0;
            }
            sw.Stop();
            Assert.True(CheckResultsOutput(parsedStrategy, sw.Elapsed.TotalSeconds, limit, window, accepted));
        }
        catch (Exception e)
        {
            Assert.Fail($"Unexpected error occurred while running basicSpamTests:{e.Message}");
        }
        finally
        {
            await repo.Delete(id);
        }
    }
    [Theory]
    [InlineData(100, new[] { 60, 60 }, new[] { 50, 20 }, new[] { "POST", "POST" }, new[] { 0, 1 }, new[] { null, "Username" }, 0, new[] { 0, 0 })] // ip 50 + username 20 -> 20
    [InlineData(100, new[] { 60, 60 }, new[] { 20, 50 }, new[] { "POST", "POST" }, new[] { 0, 1 }, new[] { null, "Username" }, 0, new[] { 0, 0 })] // ordinea inversata -> tot 20
    [InlineData(100, new[] { 120, 60 }, new[] { 30, 10 }, new[] { "POST", "POST" }, new[] { 0, 1 }, new[] { null, "Username" }, 0, new[] { 1, 1 })] // sliding log pe ambele -> 10
    [InlineData(150, new[] { 60, 90, 120 }, new[] { 100, 40, 70 }, new[] { "POST", "POST", "POST" }, new[] { 0, 1, 0 }, new[] { null, "Username", null }, 0, new[] { 0, 1, 3 })] // fixed + log + counter -> 40
    [InlineData(100, new[] { 60, 60 }, new[] { 25, 60 }, new[] { "POST", "POST" }, new[] { 0, 1 }, new[] { null, "Username" }, 0, new[] { 2, 0 })] // bucket 25 -> intre 25 si 25 + refill
    [InlineData(100, new[] { 60, 60 }, new[] { 30, 12 }, new[] { "POST", "POST" }, new[] { 1, 1 }, new[] { "Username", "Password" }, 0, new[] { 0, 1 })] // doua campuri din body -> 12
    [InlineData(150, new[] { 60, 60, 90 }, new[] { 40, 35, 45 }, new[] { "POST", "POST", "POST" }, new[] { 0, 1, 1 }, new[] { null, "Username", "Password" }, 0, new[] { 0, 0, 3 })] // trei reguli -> 35
    [InlineData(10, new[] { 60, 60 }, new[] { 100, 100 }, new[] { "POST", "POST" }, new[] { 0, 1 }, new[] { null, "Username" }, 0, new[] { 0, 1 })] // 20 requesturi < limita -> trec toate 20
    [InlineData(100, new[] { 120,60 }, new[] { 120,100 }, new[] { "POST","POST" }, new[] { 1,0 }, new[] { "Username",null }, 0, new[] { 0,2 })]
    [InlineData(200, new[] { 130,30 }, new[] { 120,200 }, new[] { "POST","POST" }, new[] { 1,1 }, new[] { "Username","Username" }, 0, new[] { 0,0 })]
    [InlineData(130, new[] { 90,60,90 }, new[] { 15,300,100 }, new[] { "POST","POST","POST" }, new[] { 1,1,1 }, new[] { "Username","Username","Username" }, 0, new[] { 2,2,2 })]
    [InlineData(120, new[] { 60,70,1000 }, new[] { 15,120,0 }, new[] { "POST","POST","POST" }, new[] { 1,0,0 }, new[] { "Username",null,null }, 0, new[] { 3,3,3 })]
    [InlineData(130, new[] { 120,110,30}, new[] { 600,300,250 }, new[] { "POST","POST","POST" }, new[] { 1,0,1 }, new[] { "Username",null,"Pass" }, 1, new[] { 3,2,3 })]
    [InlineData(60, new[] { 60,30,20,10,20 }, new[] { 200,300,100,70,80 }, new[] { "POST","POST","POST","POST","POST" }, new[] { 0,0,1,1,0 }, new[] { null,null,"Username","Hacker-injected-cookie",null }, 0, new[] { 2,2,3,1,3 })]
    [InlineData(120, new[] { 120,90,100 }, new[] { 300,180,250 }, new[] {"POST","POST","POST"  }, new[] { 1,1,1 }, new[] { "Username","Username","Username" }, 1, new[] { 3,3,3 })]
    [InlineData(300, new[] { 100 }, new[] { 500 }, new[] { "POST" }, new[] { 1 }, new[] { "Username" }, 1, new[] { 3 })]
    [InlineData(400, new[] { 60 }, new[] { 15 }, new[] { "POST" }, new[] { 1 }, new[] { "Username" }, 0, new[] { 2 })]
    [InlineData(120, new[] { 60 }, new[] { 15 }, new[] { "POST" }, new[] { 1 }, new[] { "Username" }, 0, new[] { 1 })]
    [InlineData(600, new[] { 60,30,40,40 }, new[] { 20,20,20,20, }, new[] { "POST","POST","POST","POST" }, new[] { 1,0,0,0 }, new[] { "Username",null,null,null }, 0, new[] { 3,3,3,3 })]
    [InlineData(240, new[] { 200,100,100 }, new[] { 5,5,5 }, new[] { "POST","POST","POST" }, new[] { 1,0,0 }, new[] { "Username",null,null }, 0, new[] { 1,1,1 })]
    public async Task SpamTestsWithMultipleAttributes(int multiple,IList<int> windows,IList<int> limits,IList<string> methods,IList<int> sources,IList<string?> fieldNames, int appEndpoint, IList<int> strategies)
    {
        /*
         * every rule gets a worker who sends multiple requests
         * all the rules are on the same endpoint,so every request goes through
         * all the rules with the same method,till one of them rejects it
         */
        IList<long> idsForDeletion = new List<long>();
        var repo = new EndpointLimitDataRepository(_db.NewScope().Context, _db.Multiplexer);
        try
        {
            IList<HttpMethod> parsedMethods = new List<HttpMethod>();
            IList<RateLimitSource> parsedSources = new List<RateLimitSource>();
            IList<RateLimitStrategy> parsedStrategies = new List<RateLimitStrategy>();
            InAppEndpoint parsedEndpoint = InAppEndpoint.InAppLogin;
            try
            {
                parsedMethods = (from method in methods select HttpMethod.Parse(method)).ToList();
                parsedSources = (from source in sources select (RateLimitSource)source).ToList();
                parsedStrategies = (from strategy in strategies select (RateLimitStrategy)strategy).ToList();
                parsedEndpoint = (InAppEndpoint)appEndpoint;
                Assert.Equal(parsedMethods.Count(), parsedSources.Count());
                Assert.Equal(parsedSources.Count(), parsedStrategies.Count());
                Assert.Equal(parsedStrategies.Count(), parsedMethods.Count());
                Assert.Equal(parsedMethods.Count(), windows.Count());
                Assert.Equal(windows.Count(), limits.Count());
                Assert.Equal(limits.Count(), fieldNames.Count());
                Assert.Equal(fieldNames.Count(), parsedMethods.Count());
            }
            catch (Exception e)
            {
                Assert.Fail($"Exception {e.Message} while parsing arguments");
            }
            int length = parsedMethods.Count();
            var workers = new List<Func<Task<HttpStatusCode>>>();
            for (int i = 0; i < length; i++)
                {
                    long id = await repo.Add(new EndpointLimitData(0, parsedMethods[i],
                        knownEndpoints[parsedEndpoint], i + 1,
                        parsedSources[i], windows[i], limits[i], fieldNames[i],
                        parsedStrategies[i]));
                    Assert.NotEqual(0, id);
                    idsForDeletion.Add(id);
                    int j = i;
                    workers.Add(async () =>
                    {
                        var body = CreateDefaultBodyDictionary(parsedEndpoint);
                        foreach (int k in Enumerable.Range(0, length))
                            if (parsedSources[k] == RateLimitSource.BodyField)
                                body[fieldNames[k]!] = "one value for basic brute force";
                        var response = await SendAndIntercept(SharedClient, parsedMethods[j],knownEndpoints[parsedEndpoint], body);
                        return response.StatusCode;
                    });
                }
                var sw = Stopwatch.StartNew();
                var results = await Task.WhenAll(Enumerable.Range(0, multiple).SelectMany(_ => workers.Select(w => w())));
                sw.Stop();
                Assert.True(sw.Elapsed.TotalSeconds < windows.Min(), $"test ran {sw.Elapsed.TotalSeconds:F1}seconds, more thatn the smallest possible window");
                //grouping requests after methods
                foreach (var group in Enumerable.Range(0, length).GroupBy(i => parsedMethods[i]))
                {
                    int[] rules = group.ToArray();
                    int sent = rules.Length * multiple;
                    int accepted = Enumerable.Range(0, results.Length).Count(r => rules.Contains(r % length) && results[r] != HttpStatusCode.TooManyRequests);
                    var (min, max) = ExpectedAcceptedForMultipleRules(rules, sent, limits.ToArray(), windows.ToArray(), parsedStrategies.ToArray(), sw.Elapsed.TotalSeconds);
                    _output.WriteLine($"{group.Key}: accepted {accepted}, expected [{min}, {max}] in {sw.Elapsed.TotalSeconds:F2}s");
                    Assert.InRange(accepted, min, max);
                }
        }
        catch (Exception e)
        {
            Assert.Fail($"Unexpected error during multiple attributes spam tests: {e.Message}");
        }
        finally{
            foreach (var id in idsForDeletion) await repo.Delete(id);
            await _db.Multiplexer.GetDatabase().ExecuteAsync("flushall","async"); 
        }
    }
}