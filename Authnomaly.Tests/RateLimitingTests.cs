using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Authnomaly.Domain;
using Authnomaly.Repositories.DatabaseRepositories;
using Xunit;
using Xunit.Abstractions;

namespace Authnomaly.Tests;
public class StaticWindowTests
{
    private ITestOutputHelper _output;
    private TestDb _db;
    public StaticWindowTests(ITestOutputHelper output)
    {
        _output = output;
        _db = new TestDb();
    }
    public enum InAppEndpoint{
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
            [InAppEndpoint.InAppLogin]="/api/Clients/login",
            [InAppEndpoint.InAppRegister]="/api/Clients/register",
            [InAppEndpoint.InAppRefreshToken]="api/Clients/refresh"
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
    private Object CreateDefaultBody(InAppEndpoint endpoint)
    {
        return endpoint switch
        {
            InAppEndpoint.InAppLogin=>new { Username = "nu conteaza", Password = "nici asta" },
            InAppEndpoint.InAppRegister=>new { Username = "nu conteaza", Password = "nici asta"},
            InAppEndpoint.InAppRefreshToken=>new {},//TODO
            /*DE ASEMENEA TO DO PT RESTUL DE ENDPOINTS*/
            _=>new {}
        };
    }
    private bool CheckResultsOutput(RateLimitStrategy strategy,double runTimeSeconds,int limit,int window,int accepted,double allowedError=10e7)
    {
        double total = (runTimeSeconds / window) * limit;
        if (runTimeSeconds % window > 0) total += limit;
        return (accepted - allowedError <= total && accepted + allowedError >= total)
                   ||
               (strategy.Equals(RateLimitStrategy.FixedWindow) && 
                accepted - allowedError <= 2 * total && accepted + allowedError >= total * 2);//fixed window edge case
    }
    [Theory]
    [InlineData(500, 60, 100, "POST",0,null, 0, 2)] 
    [InlineData(500, 60, 100, "POST",0,null, 0, 0)] //static window on login
    [InlineData(500, 120, 300, "POST",0,null, 0, 0)]
    [InlineData(300, 60, 500, "POST",0,null, 1, 1)]
    [InlineData(400, 10, 500, "POST",0,null, 0, 1)]
    [InlineData(200, 10, 500, "POST",0,null, 1, 0)]
    [InlineData(600, 45, 15, "POST",0,null, 1, 0)]
    [InlineData(200, 60, 500, "POST",0,null, 1, 1)]
    [InlineData(500, 10, 500, "POST",0,null, 0, 1)]
    [InlineData(400, 10, 500, "POST",0,null, 1, 0)]
    [InlineData(350, 120, 300, "POST",0,null, 1, 0)]
    [InlineData(500, 60, 500, "POST",0,null, 1, 1)]
    [InlineData(300, 10, 500, "POST",0,null, 0, 1)]
    [InlineData(270, 10, 500, "POST",0,null, 1, 0)]
    public async Task basicSpamTests(int multiple, int window, int limit, string method,int source,string? fieldName,int appEndpoint, int strategy)
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
            Assert.True(CheckResultsOutput(parsedStrategy,sw.Elapsed.TotalSeconds,limit,window,accepted));
        }
        catch(Exception e)
        {
            Assert.Fail($"Unexpected error occurred while running basicSpamTests:{e.Message}");
        }
        finally
        {
            await repo.Delete(id);
        }
    }
}