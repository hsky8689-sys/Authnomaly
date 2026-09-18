using System.Net;
using Authnomaly.Domain;
using Authnomaly.Services;
using Authnomaly.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Authnomaly.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController:ControllerBase
{
    private readonly JwtService _jwtService;
    public DashboardController(JwtService jwtService)
    {
        _jwtService = jwtService;
    }
    [HttpDelete("admins/{id}")]
    [Authorize(Policy = "RequireAdmin")]
    public IActionResult DeleteAdmin([FromQuery] long id)
    {
        return new AcceptedAtActionResult("eqw","UsersController","ewq","ew");
    }
}