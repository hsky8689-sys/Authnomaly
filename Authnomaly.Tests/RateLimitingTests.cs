using System.Net;
using System.Text.Json;
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
[Collection("Ratelimiting")]
public class StaticWindowTests : IRateLimitTests
{
    private ITestOutputHelper _output;
    public StaticWindowTests(ITestOutputHelper output)
    {
        _output = output;
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
        var task = async () =>
        {
            HttpClient client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
            var response =await SendAndIntercept(
                client, 
                HttpMethod.Post,
                "/api/Clients/login",
                new { Username="nu conteaza", Password="nici asta" }
                );
            return response.StatusCode;
        };
        var clientList = Enumerable.Repeat(task, multiple)
            .Select(task =>
        {
            return task.Invoke();
        });
        int accepted = 0;
        foreach (var result in  await Task.WhenAll(clientList))
        {
            accepted +=result.CompareTo(HttpStatusCode.TooManyRequests)==0?1:0;
        }
        Assert.Equal(100,multiple-accepted);
    }
    public Task SignUpTests()
    {
        throw new NotImplementedException();
    }
    public Task RegisterTests()
    {
        throw new NotImplementedException();
    }
}