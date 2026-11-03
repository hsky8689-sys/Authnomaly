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

    private Dictionary<string, object?> CreateDefaultBody(InAppEndpoint endpoint)
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
                    CreateDefaultBody(parsedEndpoint)
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
/*
    [Theory]
    [InlineData(100, new[] { 60, 60 }, new[] { 50, 20 }, new[] { "POST", "POST" }, new[] { 0, 1 },
        new[] { null, "Username" }, 0, new[] { 0, 0 })] // ip 50 + username 20 -> 20
    [InlineData(100, new[] { 60, 60 }, new[] { 20, 50 }, new[] { "POST", "POST" }, new[] { 0, 1 },
        new[] { null, "Username" }, 0, new[] { 0, 0 })] // ordinea inversata -> tot 20
    [InlineData(100, new[] { 120, 60 }, new[] { 30, 10 }, new[] { "POST", "POST" }, new[] { 0, 1 },
        new[] { null, "Username" }, 0, new[] { 1, 1 })] // sliding log pe ambele -> 10
    [InlineData(150, new[] { 60, 90, 120 }, new[] { 100, 40, 70 }, new[] { "POST", "POST", "POST" }, new[] { 0, 1, 0 },
        new[] { null, "Username", null }, 0, new[] { 0, 1, 3 })] // fixed + log + counter -> 40
    [InlineData(100, new[] { 60, 60 }, new[] { 25, 60 }, new[] { "POST", "POST" }, new[] { 0, 1 },
        new[] { null, "Username" }, 0, new[] { 2, 0 })] // bucket 25 -> intre 25 si 25 + refill
    [InlineData(100, new[] { 60, 60 }, new[] { 30, 12 }, new[] { "POST", "POST" }, new[] { 1, 1 },
        new[] { "Username", "Password" }, 0, new[] { 0, 1 })] // doua campuri din body -> 12
    [InlineData(150, new[] { 60, 60, 90 }, new[] { 40, 35, 45 }, new[] { "POST", "POST", "POST" }, new[] { 0, 1, 1 },
        new[] { null, "Username", "Password" }, 0, new[] { 0, 0, 3 })] // trei reguli -> 35
    [InlineData(10, new[] { 60, 60 }, new[] { 100, 100 }, new[] { "POST", "POST" }, new[] { 0, 1 },
        new[] { null, "Username" }, 0, new[] { 0, 1 })] // 20 requesturi < limita -> trec toate 20
    [InlineData(100, new[] { 60 }, new[] { 15 }, new[] { "POST" }, new[] { 1 }, new[] { "Username" }, 0, new[] { 0 })]
    public async Task SpamTestsWithMultipleAttributes(int multiple,
        IList<int> windows,
        IList<int> limits,
        IList<string> methods,
        IList<int> sources,
        IList<string?> fieldNames,
        int appEndpoint,
        IList<int> strategies)
    {
        /*
         * every rule gets a worker who sends multiple requests
         * all the rules are on the same endpoint,so every request goes through
         * all the rules with the same method,till one of them rejects it
         
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
            long id = -1;
            try
            {
                var workers = new List<Func<Task<HttpStatusCode>>>();
                for (int i = 0; i < length; i++)
                {
                    int j = i;
                    id = await repo.Add(new EndpointLimitData(0, parsedMethods[i],
                        knownEndpoints[parsedEndpoint], i + 1,
                        parsedSources[i], windows[i], limits[i], fieldNames[i],
                        parsedStrategies[i]));
                    idsForDeletion.Add(id);
                    Func<Task<HttpStatusCode>> worker = async () =>
                    {
                        HttpClient client = new HttpClient(new SocketsHttpHandler
                        {
                            MaxConnectionsPerServer = 256
                        }) { BaseAddress = new Uri("http://localhost:5000") };
                        var body = CreateDefaultBody(parsedEndpoint);
                        HttpMethod current = parsedMethods[j];
                        if (parsedSources[j].Equals(RateLimitSource.BodyField))
                            body?.Add(fieldNames[j]!, "one value for basic brute force");
                        var response = await SendAndIntercept(
                            client,
                            current,
                            knownEndpoints[parsedEndpoint],
                            body
                        );
                        return response.StatusCode;
                    };
                    workers.Add(worker);
                }

                var clientList = Enumerable.Repeat(clients, multiple).Select(tasks => tasks.Select(t => t.Invoke()));
                int[] accepted = new int[multiple];
                var sw = Stopwatch.StartNew();
                foreach (var iteration in clientList)
                {
                    int i = 0;
                    foreach (var result in await Task.WhenAll(iteration))
                    {
                        accepted[i++] += result.CompareTo(HttpStatusCode.TooManyRequests) != 0 ? 1 : 0;
                    }
                }

                sw.Stop();
                for (int i = 0; i < multiple; i++)
                {
                    Assert.True(CheckResultsOutput(parsedStrategies[i], sw.Elapsed.TotalSeconds, limits[i], windows[i],
                        accepted[i]));
                }
            }
            finally
            {
                foreach (var l in idsForDeletion)
                {
                    await repo.Delete(l);
                }
            }
    }*/
}