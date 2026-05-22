using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MojTermin.Api.Application.Interfaces;
using MojTermin.Api.Domain.Entities;

namespace MojTermin.Api.Infrastructure.Services;

public class JwtTokenService(IOptions<JwtOptions> jwtOptions) : IJwtTokenService
{
    public (string Token, DateTime ExpiresAtUtc) GenerateToken(AppUser user)
    {
        var options = jwtOptions.Value;
        var expires = DateTime.UtcNow.AddMinutes(options.ExpirationMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("businessId", user.BusinessId.ToString()),
            new(ClaimTypes.Role, user.Role),
            new(ClaimTypes.Name, user.FullName)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        var token = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
        return (token, expires);
    }
}

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "MojTermin.Api";
    public string Audience { get; set; } = "MojTermin.Client";
    public string SecretKey { get; set; } = "CHANGE_ME_TO_A_LONG_SECRET_KEY_32_CHARACTERS_MIN";
    public int ExpirationMinutes { get; set; } = 480;
}
