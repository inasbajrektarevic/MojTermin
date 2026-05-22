using MojTermin.Api.Domain.Entities;

namespace MojTermin.Api.Application.Interfaces;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAtUtc) GenerateToken(AppUser user);
}
