using System.Net;
using Authnomaly.Domain;
using Authnomaly.Services;
using Authnomaly.Utils;
using Microsoft.AspNetCore.Mvc;

namespace Authnomaly.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController:ControllerBase
{
    private readonly AuthService _authService;
    public UsersController(AuthService authService)
    {
        _authService = authService;
    }
    [HttpPost("login")]
    public async void HandleLogin([FromBody] string username,[FromBody] string password)
    {
        LoginAttempt newEntry = DeviceDetails.CollectAttemptData(HttpContext);
        newEntry.Username = username;
        try
        {
            User? found = await _authService.Login(username, password,newEntry);
            //var jwt = JwtService.CreateJwt(found.id);
        }
        catch
        {
            throw;
        }
    }
}