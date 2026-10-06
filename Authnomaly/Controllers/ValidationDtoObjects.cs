using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Extensions;

namespace Authnomaly.Controllers;

public class LoginRequest
{
    [Required]
    public string Username
    {get;set;} = "";
    public string Password
    { get; set; } = "";
}
public class RegisterRequest : LoginRequest
{
    [Required] public string Email { get; set; } = "";
}

public class RateLimitData
{
    [Required] public IPAddress Adress { get; set; } = IPAddress.Loopback;
    [Required] public string Url { get; set; } = "";
    [Required] public string Username { get; set; } = "";
    public RateLimitData(HttpContext context)
    {
        Url = context.Request.GetDisplayUrl();
        Adress = context.Connection.RemoteIpAddress ?? IPAddress.Loopback;
        var dictionary = new Dictionary<string, string>();
        try
        {
            var json = JsonSerializer.Serialize(context.Request.Body);
            dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch(Exception e)
        {
            Console.WriteLine(e.Message);
            dictionary["Username"] = "";
        }
        Username = dictionary["Username"];
    }
}