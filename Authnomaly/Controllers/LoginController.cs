using Authnomaly.Domain;
using Authnomaly.Services;
using Authnomaly.Utils;
using Microsoft.AspNetCore.Mvc;

namespace Authnomaly.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClientsController:ControllerBase
{
    private readonly AuthService _authService;
    private readonly JwtService _jwtService;
    private readonly DataProtectionAPIService _protectionApiService;
    public ClientsController(AuthService authService,
                             JwtService jwtService,
                             DataProtectionAPIService protectionApiService)
    {
        _authService = authService;
        _jwtService = jwtService;
        _protectionApiService = protectionApiService;
    }
    [HttpPost("login")]
    public async Task<IActionResult> HandleLogin([FromBody] IDictionary<string,string> loginCredentials)
    {
        var username = loginCredentials["username"];
        var password = loginCredentials["password"];
        LoginAttempt newEntry = DeviceDetails.CollectAttemptData(HttpContext);
        newEntry.Username = username;
        User? found = await _authService.Login(username, password,newEntry);
        if (found.Id == 0) return BadRequest(new { message = "Wrong credentials" });
        var lastSigning = await _jwtService.GetLastPrivateKey();
        var lastPrivate = _protectionApiService.GetPrivateKey(lastSigning);
        var jwt = JwtUtils.CreateJwt(username,Guid.NewGuid(),lastPrivate);
        return Ok(new {token=jwt,message=$"Login successful for user {found.Username}"});
    }
    [HttpPost("register")]
    public async Task<IActionResult> HandleReqister([FromBody] IDictionary<string,string> registerData)
    {
        var username = registerData["username"];
        var password = registerData["password"];
        var email = registerData["email"];
        var registered = await _authService.Authenticate(username, password, email);
        return registered.authenticated 
                ? Ok(new { message = "User was succesfully created" })
                : BadRequest(new {message=registered.message});
    }

    [HttpGet("refresh")]
    public async Task<IActionResult> HandleRefresh([FromBody] string jti)
    {
        return Ok();
    }
}