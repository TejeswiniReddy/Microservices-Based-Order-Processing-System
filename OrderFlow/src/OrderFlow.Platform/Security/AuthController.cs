using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Contracts.Dtos;

namespace OrderFlow.Platform.Security;

/// <summary>
/// Issues signed JWTs. Mounted into every host that references the platform.
/// </summary>
[ApiController]
[Route("auth")]
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    private readonly UserStore _users;
    private readonly JwtTokenService _tokens;

    public AuthController(UserStore users, JwtTokenService tokens)
    {
        _users = users;
        _tokens = tokens;
    }

    [HttpPost("token")]
    public ActionResult<TokenResponse> Token([FromBody] LoginRequest request)
    {
        var user = _users.Validate(request.Username, request.Password);
        if (user is null)
            return Unauthorized(new { error = "invalid_credentials" });

        var (token, expires) = _tokens.CreateToken(user.UserId, user.Username, user.Role);
        return Ok(new TokenResponse(token, "Bearer", expires, user.Role));
    }
}
