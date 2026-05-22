using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MojTermin.Api.Application.DTOs;
using MojTermin.Api.Application.Interfaces;
using MojTermin.Api.Domain.Entities;
using MojTermin.Api.Domain.Enums;
using MojTermin.Api.Infrastructure.Data;
using MojTermin.Api.Infrastructure.Services;
using System.Security.Cryptography;

namespace MojTermin.Api.API.Controllers;

[ApiController]
[Route("api/businesses")]
[Authorize(Policy = "OwnerOnly")]
public class BusinessesController(
    MojTerminDbContext dbContext,
    ICurrentBusinessService currentBusinessService,
    IAdminAuditService adminAuditService,
    IPasswordHasher<AppUser> passwordHasher,
    IOptions<AuthOptions> authOptions,
    IOptions<ClientAppOptions> clientAppOptions,
    IOptions<NotificationOptions> notificationOptions,
    IWebHostEnvironment environment,
    INotificationService notificationService,
    ILogger<BusinessesController> logger) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<RegisterBusinessResponseDto>> RegisterBusiness([FromBody] RegisterBusinessRequestDto dto)
    {
        if (!authOptions.Value.AllowPublicRegistration)
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Registracija novih biznisa je trenutno onemogućena.");
        }

        var normalizedSlug = dto.Slug.Trim().ToLowerInvariant();
        var ownerEmail = dto.OwnerEmail.Trim().ToLowerInvariant();
        var ownerUsername = dto.OwnerUsername.Trim().ToLowerInvariant();
        var businessEmail = dto.BusinessEmail.Trim().ToLowerInvariant();

        var slugTaken = await dbContext.Businesses.AnyAsync(x => x.Slug == normalizedSlug);
        if (slugTaken)
        {
            return Conflict("Odabrani slug je već zauzet.");
        }

        var ownerEmailTaken = await dbContext.AppUsers.AnyAsync(x => x.Email == ownerEmail);
        if (ownerEmailTaken)
        {
            return Conflict("Owner email je već zauzet.");
        }

        var ownerUsernameTaken = await dbContext.AppUsers.AnyAsync(x => x.Username == ownerUsername);
        if (ownerUsernameTaken)
        {
            return Conflict("Owner username je već zauzet.");
        }

        var business = new Domain.Entities.Business
        {
            Id = Guid.NewGuid(),
            Name = dto.BusinessName.Trim(),
            Slug = normalizedSlug,
            BusinessType = dto.BusinessType,
            Phone = dto.Phone.Trim(),
            Email = businessEmail,
            Address = dto.Address.Trim(),
            Description = dto.Description.Trim(),
            LogoUrl = dto.LogoUrl?.Trim(),
            CoverImageUrl = string.IsNullOrWhiteSpace(dto.CoverImageUrl)
                ? GetDefaultCoverImageUrl(dto.BusinessType)
                : dto.CoverImageUrl.Trim(),
            ThemePreset = GetThemePreset(dto.BusinessType),
            PrimaryColor = GetPrimaryColor(dto.BusinessType),
            SecondaryColor = GetSecondaryColor(dto.BusinessType),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Strict email verification: the owner row is created up-front so the
        // username/email uniqueness check above is meaningful, but EmailVerified
        // stays false and a verification token is minted. The /api/auth/login
        // endpoint refuses to issue tokens for unverified owners.
        var rawVerificationToken = GenerateVerificationTokenString();
        var verificationLifetime = TimeSpan.FromHours(Math.Max(1, clientAppOptions.Value.EmailVerificationTokenLifetimeHours));

        var owner = new AppUser
        {
            Id = Guid.NewGuid(),
            BusinessId = business.Id,
            FullName = dto.OwnerFullName.Trim(),
            Email = ownerEmail,
            Username = ownerUsername,
            Role = "Owner",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            EmailVerified = false,
            EmailVerificationTokenHash = EmailVerificationTokenHasher.Hash(rawVerificationToken),
            EmailVerificationTokenExpiresAtUtc = DateTime.UtcNow.Add(verificationLifetime)
        };
        owner.PasswordHash = passwordHasher.HashPassword(owner, dto.OwnerPassword);

        dbContext.Businesses.Add(business);
        dbContext.AppUsers.Add(owner);
        dbContext.WorkingHours.AddRange(BuildDefaultWorkingHours(business.Id));
        await dbContext.SaveChangesAsync();
        await adminAuditService.LogAsync(
            business.Id,
            action: "register",
            resourceType: "business",
            resourceId: business.Id,
            summary: $"Registrovan biznis: {business.Name}",
            metadata: new { Owner = owner.Email, business.Slug });

        // Send the verification email AFTER SaveChanges so we never email a user
        // whose row failed to persist (e.g. due to a transient FK violation).
        var verificationUrl = BuildVerificationUrl(rawVerificationToken);
        var emailDispatched = NotificationDispatch.IsConfigured(notificationOptions.Value);
        try
        {
            await notificationService.SendEmailVerificationAsync(
                business.Id,
                owner.Email,
                owner.FullName,
                business.Name,
                verificationUrl);
        }
        catch (Exception ex)
        {
            // Don't fail the registration if the email queue chokes — the owner
            // can still request a fresh link from the /resend-verification
            // endpoint. We log loudly so ops sees the broken SMTP config fast.
            emailDispatched = false;
            logger.LogError(ex, "Failed to enqueue verification email for owner {OwnerId} of business {BusinessId}.", owner.Id, business.Id);
        }

        if (!emailDispatched && environment.IsDevelopment())
        {
            logger.LogWarning(
                "SMTP nije podešen (Notifications:Enabled ili SMTP polja). Dev verifikacioni link: {VerificationUrl}",
                verificationUrl);
        }

        var message = emailDispatched
            ? "Verifikacioni link je poslan na vaš email. Provjerite inbox (i spam folder) i kliknite na link da završite registraciju."
            : environment.IsDevelopment()
                ? "Nalog je kreiran, ali email nije poslan jer SMTP nije podešen na API-ju. Koristite dev link na ekranu ili uključite Notifications u appsettings."
                : "Nalog je kreiran. Email verifikacije nije poslan — kontaktirajte podršku ili pokušajte „Pošalji link ponovo“.";

        return Ok(new RegisterBusinessResponseDto
        {
            BusinessId = business.Id,
            BusinessSlug = business.Slug,
            OwnerEmail = owner.Email,
            OwnerFullName = owner.FullName,
            RequiresEmailVerification = true,
            EmailDispatched = emailDispatched,
            DevVerificationUrl = !emailDispatched && environment.IsDevelopment() ? verificationUrl : null,
            Message = message
        });
    }

    private string BuildVerificationUrl(string rawToken)
    {
        var baseUrl = (clientAppOptions.Value.BaseUrl ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            // Fallback so the email link still works in dev when ClientApp__BaseUrl
            // is not set. In production the missing value would be flagged by the
            // startup config check, not handled silently.
            baseUrl = "http://localhost:4200";
        }
        return $"{baseUrl}/verify-email?token={Uri.EscapeDataString(rawToken)}";
    }

    private static string GenerateVerificationTokenString()
    {
        // 48 random bytes → 64-char URL-safe base64. Plenty of entropy and fits
        // comfortably in the 200-char DTO ceiling without truncation.
        Span<byte> bytes = stackalloc byte[48];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    [HttpGet("current")]
    public async Task<ActionResult<BusinessDto>> GetCurrent()
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var business = await dbContext.Businesses.FirstOrDefaultAsync(x => x.Id == businessId);
        if (business is null)
        {
            return NotFound("Business nije pronađen.");
        }

        return Ok(MapToDto(business));
    }

    [HttpPut("current")]
    public async Task<ActionResult<BusinessDto>> UpdateCurrent([FromBody] UpdateBusinessDto dto)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var business = await dbContext.Businesses.FirstOrDefaultAsync(x => x.Id == businessId);
        if (business is null)
        {
            return NotFound("Business nije pronađen.");
        }

        var slugExists = await dbContext.Businesses
            .AnyAsync(x => x.Slug == dto.Slug && x.Id != businessId);
        if (slugExists)
        {
            return Conflict("Odabrani slug je već zauzet.");
        }

        business.Name = dto.Name.Trim();
        business.Slug = dto.Slug.Trim().ToLowerInvariant();
        business.BusinessType = dto.BusinessType;
        business.Phone = dto.Phone.Trim();
        business.Email = dto.Email.Trim();
        business.Address = dto.Address.Trim();
        business.Description = dto.Description.Trim();
        business.LogoUrl = dto.LogoUrl?.Trim();
        business.CoverImageUrl = dto.CoverImageUrl?.Trim();
        business.ThemePreset = string.IsNullOrWhiteSpace(dto.ThemePreset)
            ? business.ThemePreset
            : dto.ThemePreset.Trim().ToLowerInvariant();
        business.PrimaryColor = dto.PrimaryColor?.Trim();
        business.SecondaryColor = dto.SecondaryColor?.Trim();

        await dbContext.SaveChangesAsync();
        await adminAuditService.LogAsync(
            businessId,
            action: "update",
            resourceType: "business-profile",
            resourceId: business.Id,
            summary: "Ažuriran poslovni profil",
            metadata: new { business.Name, business.Slug, business.Email, business.Phone });
        return Ok(MapToDto(business));
    }

    [HttpGet("by-slug/{slug}")]
    [AllowAnonymous]
    public async Task<ActionResult<BusinessDto>> GetBySlug(string slug)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var business = await dbContext.Businesses
            .FirstOrDefaultAsync(x => x.Slug == normalizedSlug && x.IsActive);

        if (business is null)
        {
            return NotFound("Business nije pronađen.");
        }

        return Ok(MapToDto(business));
    }

    private static BusinessDto MapToDto(Domain.Entities.Business business) => new()
    {
        Id = business.Id,
        Name = business.Name,
        Slug = business.Slug,
        BusinessType = business.BusinessType,
        Phone = business.Phone,
        Email = business.Email,
        Address = business.Address,
        Description = business.Description,
        LogoUrl = business.LogoUrl,
        CoverImageUrl = business.CoverImageUrl,
        ThemePreset = business.ThemePreset,
        PrimaryColor = business.PrimaryColor,
        SecondaryColor = business.SecondaryColor,
        IsActive = business.IsActive,
        CreatedAt = business.CreatedAt
    };

    private static string GetThemePreset(BusinessType businessType) => businessType switch
    {
        BusinessType.BeautySalon => "beauty",
        BusinessType.DentalClinic => "dental",
        BusinessType.CarService => "auto",
        BusinessType.Apartment => "apartment",
        BusinessType.Fitness => "fitness",
        _ => "default"
    };

    private static string GetPrimaryColor(BusinessType businessType) => businessType switch
    {
        BusinessType.BeautySalon => "#7c3aed",
        BusinessType.DentalClinic => "#0ea5e9",
        BusinessType.CarService => "#2563eb",
        BusinessType.Apartment => "#0f766e",
        BusinessType.Fitness => "#dc2626",
        _ => "#1d4ed8"
    };

    private static string GetSecondaryColor(BusinessType businessType) => businessType switch
    {
        BusinessType.BeautySalon => "#ec4899",
        BusinessType.DentalClinic => "#14b8a6",
        BusinessType.CarService => "#0284c7",
        BusinessType.Apartment => "#059669",
        BusinessType.Fitness => "#f97316",
        _ => "#6366f1"
    };

    private static string GetDefaultCoverImageUrl(BusinessType businessType) => businessType switch
    {
        BusinessType.BeautySalon => "/images/covers/beauty-cover.svg",
        BusinessType.DentalClinic => "/images/covers/dental-cover.svg",
        BusinessType.CarService => "/images/covers/car-cover.svg",
        BusinessType.Apartment => "/images/covers/apartment-cover.svg",
        BusinessType.Fitness => "/images/covers/fitness-cover.svg",
        _ => "/images/covers/default-cover.svg"
    };

    private static IEnumerable<WorkingHour> BuildDefaultWorkingHours(Guid businessId)
    {
        return
        [
            new() { Id = Guid.NewGuid(), BusinessId = businessId, DayOfWeek = DayOfWeek.Monday, OpenTime = new TimeSpan(8, 0, 0), CloseTime = new TimeSpan(16, 0, 0), IsClosed = false },
            new() { Id = Guid.NewGuid(), BusinessId = businessId, DayOfWeek = DayOfWeek.Tuesday, OpenTime = new TimeSpan(8, 0, 0), CloseTime = new TimeSpan(16, 0, 0), IsClosed = false },
            new() { Id = Guid.NewGuid(), BusinessId = businessId, DayOfWeek = DayOfWeek.Wednesday, OpenTime = new TimeSpan(8, 0, 0), CloseTime = new TimeSpan(16, 0, 0), IsClosed = false },
            new() { Id = Guid.NewGuid(), BusinessId = businessId, DayOfWeek = DayOfWeek.Thursday, OpenTime = new TimeSpan(8, 0, 0), CloseTime = new TimeSpan(16, 0, 0), IsClosed = false },
            new() { Id = Guid.NewGuid(), BusinessId = businessId, DayOfWeek = DayOfWeek.Friday, OpenTime = new TimeSpan(8, 0, 0), CloseTime = new TimeSpan(16, 0, 0), IsClosed = false },
            new() { Id = Guid.NewGuid(), BusinessId = businessId, DayOfWeek = DayOfWeek.Saturday, OpenTime = new TimeSpan(9, 0, 0), CloseTime = new TimeSpan(14, 0, 0), IsClosed = false },
            new() { Id = Guid.NewGuid(), BusinessId = businessId, DayOfWeek = DayOfWeek.Sunday, OpenTime = new TimeSpan(7, 0, 0), CloseTime = new TimeSpan(12, 0, 0), IsClosed = true }
        ];
    }

}
