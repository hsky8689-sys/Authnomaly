using System.Net;
using System.Text;
using System.Text.Json;
using Authnomaly.Domain;
using Authnomaly.Repositories.DatabaseRepositories;
using Xunit;
using Xunit.Abstractions;

namespace Authnomaly.Tests;

public interface IRateLimitTests
{
    //tests for our app's predefined rate limitted endpoints
    public Task LoginTests(int multiple);
    public Task RegisterTests(int multiple);
    public Task RefreshTests(int multiple);
}
public class StaticWindowTests : IRateLimitTests
{
    private ITestOutputHelper _output;
    private TestDb _db;
    public StaticWindowTests(ITestOutputHelper output)
    {
        _output = output;
        _db = new TestDb();
    }
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
    [Theory]
    [InlineData(1000)]
    //[InlineData(1000)]
    //[InlineData(10000)]
    //[InlineData(100000)]
    //[InlineData(1000000)]
    public async Task LoginTests(int multiple)
    {
        var redis = _db.NewRedis();
        var repo = new EndpointLimitDataRepository(_db.NewScope().Context,_db.Multiplexer);
        long id = -1;
        try
        {
            try
            {
                id = await repo.Add(new EndpointLimitData(0, HttpMethod.Post,
                    "/api/Clients/login", 1,
                    RateLimitSource.Ip, 60, 100, null,
                    RateLimitStrategy.SlidingWindow));
            }
            catch
            {
                await repo.Delete(id);
            }
            Assert.NotEqual(0,id);
            _output.WriteLine(id.ToString());
            var task = async () =>
            {
                HttpClient client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
                var response = await SendAndIntercept(
                    client,
                    HttpMethod.Post,
                    "/api/Clients/login",
                    new { Username = "nu conteaza", Password = "nici asta" }
                );
                return response.StatusCode;
            };
            var clientList = Enumerable.Repeat(task, multiple)
                .Select(task => { return task.Invoke(); });
            int accepted = 0;
            foreach (var result in await Task.WhenAll(clientList))
            {
                accepted += result.CompareTo(HttpStatusCode.TooManyRequests) != 0 ? 1 : 0;
            }
            Assert.Equal(100,accepted);
        }
        finally
        {
            await repo.Delete(id);
        }
    }
    [Theory]
    [InlineData(1)]
    //[InlineData(10000)]
    //[InlineData(100000)]
    //[InlineData(1000000)]
    public async Task RegisterTests(int multiple)
    {
        var repo = new EndpointLimitDataRepository(_db.NewScope().Context,_db.Multiplexer);
        long id = -1;
        try
        {
            try
            {
                id = await repo.Add(new EndpointLimitData(0, HttpMethod.Post,
                    "/api/Clients/register", 1,
                    RateLimitSource.Ip, 2, 100, null,
                    RateLimitStrategy.FixedWindow));
            }
            catch
            {
                await repo.Delete(id);
            }
            Assert.NotEqual(0,id);
            _output.WriteLine(id.ToString());
            var task = async () =>
            {
                HttpClient client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
                var response = await SendAndIntercept(
                    client,
                    HttpMethod.Post,
                    "/api/Clients/register",
                    new { Username = "nu conteaza", Password = "nici asta" ,Email="ionica.batman@ubbcluj.ro"}
                );
                return response.StatusCode;
            };
            var clientList = Enumerable.Repeat(task, multiple)
                .Select(task => { return task.Invoke(); });
            int accepted = 0;
            foreach (var result in await Task.WhenAll(clientList))
            {
                accepted += result.CompareTo(HttpStatusCode.TooManyRequests) == 0 ? 1 : 0;
            }
            Assert.Equal(100,accepted);
            accepted = 0;
            _db.NewRedis().StringSet("rl:9002:127.0.0.1",0);
            var secondClientList = Enumerable.Repeat(task, multiple).Select(task => { return task.Invoke(); });
            foreach (var result in await Task.WhenAll(secondClientList))
            {
                accepted += result.CompareTo(HttpStatusCode.TooManyRequests) == 0 ? 1 : 0;
            }
            Thread.Sleep(TimeSpan.FromMinutes(1));
            Assert.Equal(0,_db.NewRedis().StringGet("rl:9002:127.0.0.1"));
            var thirdClientList = Enumerable.Repeat(task, multiple).Select(task => { return task.Invoke(); });
            foreach (var result in await Task.WhenAll(thirdClientList))
            {
                accepted += result.CompareTo(HttpStatusCode.TooManyRequests) == 0 ? 1 : 0;
            }
            Assert.Equal(200, accepted);
        }
        finally
        {
            _db.NewRedis().StringSet("rl:9002:127.0.0.1",0);
            await repo.Delete(id);
        }
    }
    [Theory]
    [InlineData(1000)]
    [InlineData(10000)]
    [InlineData(100000)]
    [InlineData(1000000)]
    public async Task RefreshTests(int multiple)
    {
        Console.WriteLine(multiple);
        return;
        //throw new NotImplementedException();
    }
}