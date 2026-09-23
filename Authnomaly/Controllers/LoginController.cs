using System.Net;
using Authnomaly.Domain;
using Authnomaly.Repositories.Interfaces;
using Authnomaly.Services;
using Authnomaly.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

namespace Authnomaly.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClientsController:ControllerBase
{
    private readonly AuthService _authService;
    private readonly JwtService _jwtService;
    private readonly DataProtectionAPIService _protectionApiService;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILocationDetector _locationDetector;
    public ClientsController(AuthService authService,
                             JwtService jwtService,
                             DataProtectionAPIService protectionApiService,
                             IConnectionMultiplexer redis,
                             ILocationDetector detector)
    {
        _authService = authService;
        _jwtService = jwtService;
        _protectionApiService = protectionApiService;
        _redis = redis;
        _locationDetector = detector;
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

    [HttpPost("refresh")]
    public async Task<IActionResult> HandleRefresh([FromBody] IDictionary<string, string> refreshData)
    {
        string? username = refreshData["username"];
        if (username is null)
            return BadRequest(new { message = "Username not found" });
        string? jti = refreshData["jti"];
        if (jti is null)
            return BadRequest(new { message = "Jti not found" });
        SigningKey lastPrivate = await _jwtService.GetLastPrivateKey();
        SecurityKey privateKey = _protectionApiService.GetPrivateKey(lastPrivate);
        var db = _redis.GetDatabase();
        var transaction = db.CreateTransaction();
        string? lastJti = await _jwtService.GetCurrentFamilyOfJti(jti);
        if (lastJti is null)
        {
            //primul token dintr-o familie noua,deci nu avem efectiv ce verifica in redis
            Guid newFamilyId = Guid.NewGuid();
            var firstJwt = JwtUtils.CreateJwt(username, newFamilyId, privateKey);
            await _jwtService.SetCurrentFamilyJti(newFamilyId, jti, new TimeSpan(1));
            var res = await transaction.ExecuteAsync();
            return res
                ? Ok(new { message = "Access and refresh tokens refreshed", jwt = firstJwt })
                : Conflict(new { message = "transaction didn't properly finish" });

        }
        if (!lastJti.Equals(jti))
        {
            //gg,token invalid,deci pa sesiune
            await _jwtService.InvalidateTokenFamily(jti, BlacklistLevel.AnomalyDetected);
            await transaction.ExecuteAsync();
            return BadRequest(new { message = "Suspicious request blocked" });
        }
        Guid familyId = Guid.Parse(lastJti);
        //abea aici impunem conditia sa nu se fi schimbat valoarea din redis
        transaction.AddCondition(Condition.StringEqual($"refreshToken:{lastJti}", jti));
        var newJwt = JwtUtils.CreateJwt(username, familyId, privateKey);
        await _jwtService.SetCurrentFamilyJti(familyId, jti, TimeSpan.FromDays(7));
        bool comitted = await transaction.ExecuteAsync();
        if (!comitted)
        {
            /*detectam distanta ip nou vs ultim ip ok < max allowed*/
            var lastKnown = await _jwtService.GetLastLocation("refresh", username);
            if (lastKnown.latitude is null || lastKnown.longitude is null)
            {
                //aici mai e un pic de gandit tbh(de fapt nu,a setat cineva valoarea noua si nu il putem detecta,gg)
                await transaction.ExecuteAsync();
                return BadRequest(new { message = "Suspicious request blocked,could not detect coordinates" });
            }
            double lastKnownLat = lastKnown.latitude!.Value;
            double lastKnownLon = lastKnown.longitude!.Value;
            var attemptData = DeviceDetails.CollectAttemptData(HttpContext);
            IPAddress? adress = attemptData.IpAddress;
            if (adress is null)
            {
                //package with suspicious data received
                await _jwtService.InvalidateTokenFamily(jti, BlacklistLevel.AnomalyDetected);
                return BadRequest(new { message = "Suspicious request blocked,could not detect IP adress" });
            }
            var coordinates = _locationDetector.GetCoordinates(adress);
            if (coordinates.Latitude is null || coordinates.Longitude is null)
            {
                await transaction.ExecuteAsync();
                return BadRequest(new { message = "Suspicious request blocked,could not detect coordinates" });
            }
            double newLat = coordinates.Latitude.Value;
            double newLon = coordinates.Longitude.Value;
            bool okDistance = _locationDetector.ComputeDistance(newLat,
                lastKnownLat,
                newLon,
                lastKnownLon
            ) < 300;
            return okDistance
                ? Ok(new { message = "Access and refresh tokens refreshed", jwt = newJwt })
                : BadRequest(new { message = "Suspicious distance between this and last succesful connection" });
        }
        return Ok(new { message = "Access and refresh tokens refreshed", jwt = newJwt });
    }
}