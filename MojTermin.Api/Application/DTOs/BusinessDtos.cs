using System.ComponentModel.DataAnnotations;
using MojTermin.Api.Domain.Enums;

namespace MojTermin.Api.Application.DTOs;

public class BusinessDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public BusinessType BusinessType { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? CoverImageUrl { get; set; }
    public string ThemePreset { get; set; } = "default";
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UpdateBusinessDto
{
    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string Slug { get; set; } = string.Empty;

    [Required]
    public BusinessType BusinessType { get; set; }

    [MaxLength(30)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(120), EmailAddress]
    public string Email { get; set; } = string.Empty;

    [MaxLength(250)]
    public string Address { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? LogoUrl { get; set; }

    [MaxLength(500)]
    public string? CoverImageUrl { get; set; }

    [MaxLength(40)]
    public string? ThemePreset { get; set; }

    [MaxLength(20)]
    public string? PrimaryColor { get; set; }

    [MaxLength(20)]
    public string? SecondaryColor { get; set; }
}

public class RegisterBusinessRequestDto
{
    [Required, MaxLength(150)]
    public string BusinessName { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string Slug { get; set; } = string.Empty;

    [Required]
    public BusinessType BusinessType { get; set; }

    [MaxLength(30)]
    public string Phone { get; set; } = string.Empty;

    [Required, MaxLength(120), EmailAddress]
    public string BusinessEmail { get; set; } = string.Empty;

    [MaxLength(250)]
    public string Address { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? LogoUrl { get; set; }

    [MaxLength(500)]
    public string? CoverImageUrl { get; set; }

    [Required, MaxLength(150)]
    public string OwnerFullName { get; set; } = string.Empty;

    [Required, MaxLength(120), EmailAddress]
    public string OwnerEmail { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string OwnerUsername { get; set; } = string.Empty;

    [Required, MinLength(6), MaxLength(100)]
    public string OwnerPassword { get; set; } = string.Empty;
}

/// <summary>
/// Returned by /api/businesses/register when strict email-verification is on.
/// The SPA uses this to redirect to a "check your email" screen instead of
/// taking the user straight to the admin dashboard. NO JWT or refresh token
/// here on purpose — the user must complete /api/auth/verify-email first.
/// </summary>
public class RegisterBusinessResponseDto
{
    public Guid BusinessId { get; set; }
    public string BusinessSlug { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string OwnerFullName { get; set; } = string.Empty;
    public bool RequiresEmailVerification { get; set; } = true;
    /// <summary>True kada je SMTP uključen i konfiguracija kompletna.</summary>
    public bool EmailDispatched { get; set; }
    /// <summary>Samo u Development kad email nije poslan — direktan link za test.</summary>
    public string? DevVerificationUrl { get; set; }
    public string Message { get; set; } = "Verifikacioni link je poslan na vaš email.";
}
