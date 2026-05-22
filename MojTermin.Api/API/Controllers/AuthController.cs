using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MojTermin.Api.Application.DTOs;
using MojTermin.Api.Application.Interfaces;
using MojTermin.Api.Domain.Entities;
using MojTermin.Api.Infrastructure.Data;
using MojTermin.Api.Infrastructure.Services;
using System.Security.Cryptography;

namespace MojTermin.Api.API.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController(
    MojTerminDbContext dbContext,
    IPasswordHasher<AppUser> passwordHasher,
    IJwtTokenService jwtTokenService,
    IOptions<AuthOptions> authOptions,
    IOptions<ClientAppOptions> clientAppOptions,
    INotificationService notificationService,
    ICurrentBusinessService currentBusinessService,
    ILogger<AuthController> logger) : ControllerBase
{
    // NOTE: A previous POST /api/auth/register endpoint was removed in P0 hardening.
    // It accepted a client-supplied BusinessId and hardcoded Role="Owner", which
    // allowed anyone (when AllowPublicRegistration was true) to attach themselves
    // as an Owner of an arbitrary existing business. New tenants are created via
    // POST /api/businesses/register, which atomically creates a business and its
    // first owner in one transaction.

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginRequestDto dto)
    {
        var input = dto.UsernameOrEmail.Trim().ToLowerInvariant();
        var user = await dbContext.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.IsActive &&
                (x.Username == input || x.Email == input));

        if (user is null)
        {
            return Unauthorized("Pogrešan username/email ili lozinka.");
        }

        var verifyResult = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            return Unauthorized("Pogrešan username/email ili lozinka.");
        }

        // Strict email-verification gate: an owner cannot mint tokens until they
        // click the link mailed at registration. The SPA recognises the EMAIL_NOT_VERIFIED
        // code and shows a "Resend verification" CTA instead of the generic error.
        if (!user.EmailVerified)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new AuthErrorDto
            {
                Code = "EMAIL_NOT_VERIFIED",
                Message = "Email adresa nije verifikovana. Provjerite inbox za verifikacioni link.",
                Email = user.Email
            });
        }

        return Ok(await IssueAuthResponseAsync(user));
    }

    [HttpPost("verify-email")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponseDto>> VerifyEmail([FromBody] VerifyEmailRequestDto dto)
    {
        var raw = (dto.Token ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return BadRequest("Token nije dostavljen.");
        }

        var tokenHash = EmailVerificationTokenHasher.Hash(raw);
        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x => x.EmailVerificationTokenHash == tokenHash);

        if (user is null)
        {
            return BadRequest(new AuthErrorDto
            {
                Code = "INVALID_TOKEN",
                Message = "Verifikacioni link nije valjan ili je već iskorišten."
            });
        }

        if (user.EmailVerified)
        {
            // Idempotent: a refresh / accidental re-click should still let the
            // owner in instead of looking like an error.
            user.EmailVerificationTokenHash = null;
            user.EmailVerificationTokenExpiresAtUtc = null;
            await dbContext.SaveChangesAsync();
            return Ok(await IssueAuthResponseAsync(user));
        }

        if (!user.EmailVerificationTokenExpiresAtUtc.HasValue ||
            user.EmailVerificationTokenExpiresAtUtc.Value <= DateTime.UtcNow)
        {
            return BadRequest(new AuthErrorDto
            {
                Code = "TOKEN_EXPIRED",
                Message = "Verifikacioni link je istekao. Tražite novi.",
                Email = user.Email
            });
        }

        user.EmailVerified = true;
        user.EmailVerifiedAtUtc = DateTime.UtcNow;
        user.EmailVerificationTokenHash = null;
        user.EmailVerificationTokenExpiresAtUtc = null;
        await dbContext.SaveChangesAsync();

        // Auto-login on first successful verification so the user lands straight
        // in the admin panel without retyping their password.
        return Ok(await IssueAuthResponseAsync(user));
    }

    [HttpPost("resend-verification")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequestDto dto)
    {
        var email = (dto.Email ?? string.Empty).Trim().ToLowerInvariant();

        // We intentionally return the same response shape whether or not the
        // email matches a real account. This stops the endpoint from being
        // abused as an account-enumeration oracle.
        var genericOk = Ok(new { message = "Ako email postoji u sistemu, novi verifikacioni link je poslan." });

        if (string.IsNullOrEmpty(email))
        {
            return genericOk;
        }

        var user = await dbContext.AppUsers
            .Include(x => x.Business)
            .FirstOrDefaultAsync(x => x.IsActive && x.Email == email);

        if (user is null || user.EmailVerified)
        {
            return genericOk;
        }

        var rawToken = GenerateVerificationTokenString();
        var lifetime = TimeSpan.FromHours(Math.Max(1, clientAppOptions.Value.EmailVerificationTokenLifetimeHours));
        user.EmailVerificationTokenHash = EmailVerificationTokenHasher.Hash(rawToken);
        user.EmailVerificationTokenExpiresAtUtc = DateTime.UtcNow.Add(lifetime);
        await dbContext.SaveChangesAsync();

        var businessName = user.Business?.Name ?? "MojTermin";
        var verificationUrl = BuildVerificationUrl(rawToken);

        try
        {
            await notificationService.SendEmailVerificationAsync(
                user.BusinessId,
                user.Email,
                user.FullName,
                businessName,
                verificationUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enqueue resent verification email for owner {OwnerId}.", user.Id);
        }

        return genericOk;
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto dto)
    {
        var email = (dto.Email ?? string.Empty).Trim().ToLowerInvariant();

        // Always answer with the same payload so the endpoint cannot be used as
        // an account-enumeration oracle. The exact phrasing matches the SPA's
        // confirmation copy.
        var genericOk = Ok(new { message = "Ako email postoji u sistemu, link za reset lozinke je poslan." });

        if (string.IsNullOrEmpty(email))
        {
            return genericOk;
        }

        var user = await dbContext.AppUsers
            .Include(x => x.Business)
            .FirstOrDefaultAsync(x => x.IsActive && x.Email == email);

        if (user is null)
        {
            return genericOk;
        }

        // Skip reset for unverified owners — they should finish email verification
        // first. Otherwise an attacker who guessed an email could trigger a reset
        // and DoS the verification flow.
        if (!user.EmailVerified)
        {
            logger.LogInformation("Password reset requested for unverified owner {OwnerId}; skipping.", user.Id);
            return genericOk;
        }

        var rawToken = GenerateVerificationTokenString();
        var lifetime = TimeSpan.FromMinutes(Math.Max(5, authOptions.Value.PasswordResetTokenLifetimeMinutes));
        user.PasswordResetTokenHash = PasswordResetTokenHasher.Hash(rawToken);
        user.PasswordResetTokenExpiresAtUtc = DateTime.UtcNow.Add(lifetime);
        await dbContext.SaveChangesAsync();

        var businessName = user.Business?.Name ?? "MojTermin";
        var resetUrl = BuildResetPasswordUrl(rawToken);

        try
        {
            await notificationService.SendPasswordResetEmailAsync(
                user.BusinessId,
                user.Email,
                user.FullName,
                businessName,
                resetUrl,
                lifetime);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enqueue password reset email for owner {OwnerId}.", user.Id);
        }

        return genericOk;
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponseDto>> ResetPassword([FromBody] ResetPasswordRequestDto dto)
    {
        var rawToken = (dto.Token ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(rawToken))
        {
            return BadRequest(new AuthErrorDto { Code = "TOKEN_MISSING", Message = "Token za reset nije prosljeđen." });
        }

        var tokenHash = PasswordResetTokenHasher.Hash(rawToken);
        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x =>
                x.IsActive &&
                x.PasswordResetTokenHash == tokenHash);

        if (user is null)
        {
            return BadRequest(new AuthErrorDto { Code = "TOKEN_INVALID", Message = "Link za reset nije validan." });
        }

        if (user.PasswordResetTokenExpiresAtUtc is null ||
            user.PasswordResetTokenExpiresAtUtc.Value <= DateTime.UtcNow)
        {
            return BadRequest(new AuthErrorDto { Code = "TOKEN_EXPIRED", Message = "Link za reset je istekao. Zatraži novi." });
        }

        user.PasswordHash = passwordHasher.HashPassword(user, dto.NewPassword);
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAtUtc = null;

        // Reset is implicit proof of inbox control, so we promote the user to
        // EmailVerified=true in case they were stuck unverified. This matches
        // the behaviour of every major SaaS.
        if (!user.EmailVerified)
        {
            user.EmailVerified = true;
            user.EmailVerifiedAtUtc = DateTime.UtcNow;
            user.EmailVerificationTokenHash = null;
            user.EmailVerificationTokenExpiresAtUtc = null;
        }

        // Force all existing sessions to re-authenticate. A password change
        // must invalidate every outstanding refresh token for this user.
        var outstandingRefreshTokens = await dbContext.RefreshTokens
            .Where(x => x.AppUserId == user.Id && x.RevokedAtUtc == null)
            .ToListAsync();
        foreach (var rt in outstandingRefreshTokens)
        {
            rt.RevokedAtUtc = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync();

        // Auto-login so the SPA lands the user directly in the admin panel —
        // they just proved control of their inbox AND chose a new password.
        return Ok(await IssueAuthResponseAsync(user));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto dto)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var user = await dbContext.AppUsers
            .FirstOrDefaultAsync(x =>
                x.Id == userId &&
                x.BusinessId == businessId &&
                x.IsActive);

        if (user is null)
        {
            return Unauthorized();
        }

        var verify = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.CurrentPassword);
        if (verify == PasswordVerificationResult.Failed)
        {
            return BadRequest(new AuthErrorDto { Code = "WRONG_PASSWORD", Message = "Trenutna lozinka nije ispravna." });
        }

        user.PasswordHash = passwordHasher.HashPassword(user, dto.NewPassword);

        // Invalidate all refresh tokens except the one currently in use isn't
        // identifiable here without extra plumbing; safest is to revoke all and
        // let the SPA re-login. The current access token (JWT) keeps working
        // until its short TTL expires, so the user is not kicked out mid-action.
        var outstandingRefreshTokens = await dbContext.RefreshTokens
            .Where(x => x.AppUserId == user.Id && x.RevokedAtUtc == null)
            .ToListAsync();
        foreach (var rt in outstandingRefreshTokens)
        {
            rt.RevokedAtUtc = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync();
        return Ok(new { message = "Lozinka je promijenjena. Ostale aktivne sesije su odjavljene." });
    }

    private string BuildResetPasswordUrl(string rawToken)
    {
        var baseUrl = (clientAppOptions.Value.BaseUrl ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "http://localhost:4200";
        }
        return $"{baseUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}";
    }

    private string BuildVerificationUrl(string rawToken)
    {
        var baseUrl = (clientAppOptions.Value.BaseUrl ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "http://localhost:4200";
        }
        return $"{baseUrl}/verify-email?token={Uri.EscapeDataString(rawToken)}";
    }

    private static string GenerateVerificationTokenString()
    {
        Span<byte> bytes = stackalloc byte[48];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponseDto>> Refresh([FromBody] RefreshTokenRequestDto dto)
    {
        var incomingRaw = dto.RefreshToken.Trim();
        var incomingHash = RefreshTokenHasher.Hash(incomingRaw);
        var tokenRecord = await dbContext.RefreshTokens
            .Include(x => x.AppUser)
            .FirstOrDefaultAsync(x => x.Token == incomingHash);

        if (tokenRecord is null || tokenRecord.AppUser is null)
        {
            return Unauthorized("Refresh token nije validan.");
        }

        if (!tokenRecord.AppUser.IsActive)
        {
            return Unauthorized("Refresh token je istekao ili je opozvan.");
        }

        if (tokenRecord.RevokedAtUtc.HasValue)
        {
            await RevokeDescendantRefreshTokensAsync(tokenRecord);
            await dbContext.SaveChangesAsync();
            return Unauthorized("Refresh token je istekao ili je opozvan.");
        }

        if (tokenRecord.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return Unauthorized("Refresh token je istekao ili je opozvan.");
        }

        tokenRecord.RevokedAtUtc = DateTime.UtcNow;
        var newRawToken = GenerateRefreshTokenString();
        var newTokenHash = RefreshTokenHasher.Hash(newRawToken);
        tokenRecord.ReplacedByToken = newTokenHash;

        var rotated = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AppUserId = tokenRecord.AppUserId,
            Token = newTokenHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(authOptions.Value.RefreshTokenExpirationDays)
        };
        dbContext.RefreshTokens.Add(rotated);
        await CleanupUserRefreshTokensAsync(tokenRecord.AppUserId);
        await dbContext.SaveChangesAsync();

        var (jwt, expiresAt) = jwtTokenService.GenerateToken(tokenRecord.AppUser);
        return Ok(new AuthResponseDto
        {
            Token = jwt,
            RefreshToken = newRawToken,
            ExpiresAtUtc = expiresAt,
            UserId = tokenRecord.AppUser.Id,
            BusinessId = tokenRecord.AppUser.BusinessId,
            FullName = tokenRecord.AppUser.FullName,
            Username = tokenRecord.AppUser.Username,
            Role = tokenRecord.AppUser.Role
        });
    }

    [HttpPost("revoke")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> Revoke([FromBody] RefreshTokenRequestDto dto)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var tokenHash = RefreshTokenHasher.Hash(dto.RefreshToken.Trim());
        var refreshToken = await dbContext.RefreshTokens
            .Include(x => x.AppUser)
            .FirstOrDefaultAsync(x =>
                x.Token == tokenHash &&
                x.AppUser != null &&
                x.AppUser.BusinessId == businessId);
        if (refreshToken is null)
        {
            return NotFound("Refresh token nije pronađen.");
        }

        if (!refreshToken.RevokedAtUtc.HasValue)
        {
            refreshToken.RevokedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        return NoContent();
    }

    private async Task<AuthResponseDto> IssueAuthResponseAsync(AppUser user)
    {
        var persistedUser = await dbContext.AppUsers.FirstAsync(x => x.Id == user.Id);
        var (token, expires) = jwtTokenService.GenerateToken(persistedUser);
        var rawRefreshToken = GenerateRefreshTokenString();
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AppUserId = persistedUser.Id,
            Token = RefreshTokenHasher.Hash(rawRefreshToken),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(authOptions.Value.RefreshTokenExpirationDays),
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.RefreshTokens.Add(refreshToken);
        await CleanupUserRefreshTokensAsync(persistedUser.Id);
        await dbContext.SaveChangesAsync();

        return new AuthResponseDto
        {
            Token = token,
            RefreshToken = rawRefreshToken,
            ExpiresAtUtc = expires,
            UserId = persistedUser.Id,
            BusinessId = persistedUser.BusinessId,
            FullName = persistedUser.FullName,
            Username = persistedUser.Username,
            Role = persistedUser.Role
        };
    }

    private static string GenerateRefreshTokenString()
    {
        Span<byte> bytes = stackalloc byte[48];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private async Task RevokeDescendantRefreshTokensAsync(RefreshToken tokenRecord)
    {
        var nowUtc = DateTime.UtcNow;
        var nextToken = tokenRecord.ReplacedByToken;
        while (!string.IsNullOrWhiteSpace(nextToken))
        {
            var child = await dbContext.RefreshTokens.FirstOrDefaultAsync(x => x.Token == nextToken);
            if (child is null)
            {
                break;
            }

            if (!child.RevokedAtUtc.HasValue)
            {
                child.RevokedAtUtc = nowUtc;
            }

            nextToken = child.ReplacedByToken;
        }
    }

    private async Task CleanupUserRefreshTokensAsync(Guid appUserId)
    {
        var nowUtc = DateTime.UtcNow;
        var cleanupThreshold = nowUtc.AddDays(-7);
        var staleTokens = await dbContext.RefreshTokens
            .Where(x =>
                x.AppUserId == appUserId &&
                (
                    x.ExpiresAtUtc <= cleanupThreshold ||
                    (x.RevokedAtUtc.HasValue && x.RevokedAtUtc <= cleanupThreshold)
                ))
            .ToListAsync();

        if (staleTokens.Count > 0)
        {
            dbContext.RefreshTokens.RemoveRange(staleTokens);
        }
    }
}
