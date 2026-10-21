using System.Net;
using System.Text.Json;
using Authnomaly.Domain;
using Authnomaly.Repositories.DatabaseRepositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Authnomaly.Tests;

public interface IRateLimitTests
{
    //tests for our app's predefined rate limitted endpoints
    public Task LoginTests(int multiple);
    public Task SignUpTests();
    public Task RegisterTests();
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
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }
        return await client.SendAsync(request);
    }
    [Theory]
    [InlineData(1000)]
    public async Task LoginTests(int multiple)
    {
        var repo = new EndpointLimitDataRepository(_db.NewScope().Context);
        long id = -1;
        try
        {
            id = await repo.Add(new EndpointLimitData(0, HttpMethod.Post,
                "/api/Clients/login", 1,
                RateLimitSource.Ip, 0, 100, null,
                RateLimitStrategy.FixedWindow));
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
                accepted += result.CompareTo(HttpStatusCode.TooManyRequests) == 0 ? 1 : 0;
            }
            Assert.Equal(100, multiple - accepted);
        }
        finally
        {
            await repo.Delete(id);
        }
    }
    [Fact]
    public Task SignUpTests()
    {
        throw new NotImplementedException();
    }
    [Fact]
    public Task RegisterTests()
    {
        throw new NotImplementedException();
    }
}