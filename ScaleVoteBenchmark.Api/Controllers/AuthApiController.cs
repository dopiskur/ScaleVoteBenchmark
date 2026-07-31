using Microsoft.AspNetCore.Mvc;
using ScaleVoteBenchmark.Api.Auth;

namespace ScaleVoteBenchmark.Api.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthApiController : ControllerBase
    {
        private readonly IConfiguration configuration;

        public AuthApiController(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        public class LoginRequest
        {
            public string Username { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        /// <summary>
        /// Provjerava administratorske kredencijale i, u slučaju uspjeha,
        /// vraća JWT token korišten za pristup zaštićenim funkcijama.
        /// </summary>
        [HttpPost("login")]
        public ActionResult<string> Login([FromBody] LoginRequest request)
        {
            string adminUsername = configuration["AdminUser:Username"] ?? string.Empty;
            string adminPassword = configuration["AdminUser:Password"] ?? string.Empty;

            if (request.Username != adminUsername || request.Password != adminPassword)
            {
                return Unauthorized("Neispravno korisničko ime ili lozinka.");
            }

            string token = JwtTokenProvider.CreateToken(
                key: configuration["Jwt:Key"]!,
                issuer: configuration["Jwt:Issuer"]!,
                audience: configuration["Jwt:Audience"]!,
                expirationMinutes: int.Parse(configuration["Jwt:ExpirationMinutes"] ?? "60"),
                username: request.Username);

            return Ok(new { token });
        }
    }
}
