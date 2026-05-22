using System.ComponentModel.DataAnnotations;
using MojTermin.Api.Domain.Enums;

namespace MojTermin.Api.Application.DTOs;

public class AppointmentDto
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ServiceId { get; set; }
    public Guid ClientId { get; set; }
    public Guid? StaffMemberId { get; set; }
    public DateTime AppointmentDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public AppointmentStatus Status { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string StaffMemberName { get; set; } = string.Empty;
}

public class PublicCreateAppointmentDto
{
    [Required]
    public Guid ServiceId { get; set; }

    [Required]
    public DateTime AppointmentDate { get; set; }

    [Required]
    public TimeSpan StartTime { get; set; }

    public Guid? StaffMemberId { get; set; }

    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? Email { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    /// <summary>
    /// Honeypot field. Real users never see or fill this — it is hidden with CSS in
    /// the frontend. Bots that auto-fill every input will populate it, which lets
    /// the server reject them silently without burdening real users with CAPTCHA.
    /// Server-side: any non-empty value is treated as a bot submission.
    /// </summary>
    [MaxLength(200)]
    public string? Website { get; set; }
}

public class PublicAppointmentAvailabilityDto
{
    public DateTime AppointmentDate { get; set; }
    public Guid ServiceId { get; set; }
    public List<TimeSpan> AvailableStartTimes { get; set; } = new();
    public List<PublicAppointmentSlotDto> Slots { get; set; } = new();
}

public class PublicAppointmentSlotDto
{
    public TimeSpan StartTime { get; set; }
    public bool IsAvailable { get; set; }
    public string? UnavailableReason { get; set; }
}

public class PublicCancelAppointmentRequestDto
{
    [Required, MaxLength(200)]
    public string Token { get; set; } = string.Empty;
}

/// <summary>
/// Returned by /api/public/appointments/cancel/lookup so the SPA can render
/// human-readable details (business name, service, when) BEFORE the user
/// confirms cancellation. Lookup does not mutate state — the actual cancel
/// happens on POST /api/public/appointments/cancel.
/// </summary>
public class PublicAppointmentSummaryDto
{
    public string BusinessName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string ClientFirstName { get; set; } = string.Empty;
    public DateTime AppointmentDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool AlreadyCancelled { get; set; }
    public bool TooLateToCancel { get; set; }
}
