using System.ComponentModel.DataAnnotations;

namespace Authnomaly.Controllers;

public class LoginRequest
{ 
    [Required] public string Username { get; set; } = "";
    [Required] public string Password { get; set; } = " ";
}

public class RegisterRequest : LoginRequest
{
    [Required] public string Email { get; set; } = "";
}
