using System.ComponentModel.DataAnnotations;

namespace MojTermin.Api.Application.DTOs;

public class LoginRequestDto
{
    [Required]
    public string UsernameOrEmail { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public Guid UserId { get; set; }
    public Guid BusinessId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class RefreshTokenRequestDto
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>
/// Sent when /api/auth/login is rejected for a reason the SPA needs to react
/// to specifically (currently just "email not verified" so we can render a
/// resend-verification action). For generic bad-credential failures the API
/// still returns a plain string body via Unauthorized().
/// </summary>
public class AuthErrorDto
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Email { get; set; }
}

public class VerifyEmailRequestDto
{
    [Required, MaxLength(200)]
    public string Token { get; set; } = string.Empty;
}

public class ResendVerificationRequestDto
{
    [Required, MaxLength(120), EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class ForgotPasswordRequestDto
{
    [Required, MaxLength(120), EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequestDto
{
    [Required, MaxLength(200)]
    public string Token { get; set; } = string.Empty;

    [Required, MinLength(6), MaxLength(200)]
    public string NewPassword { get; set; } = string.Empty;
}

public class ChangePasswordRequestDto
{
    [Required, MaxLength(200)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(6), MaxLength(200)]
    public string NewPassword { get; set; } = string.Empty;
}
