using MojTermin.Api.Domain.Enums;

namespace MojTermin.Api.Application.Interfaces;

public interface INotificationService
{
    Task NotifyNewAppointmentRequestAsync(
        Guid businessId,
        Guid appointmentId,
        string businessName,
        string? businessEmail,
        string clientName,
        string clientPhone,
        string? clientEmail,
        string serviceName,
        DateTime appointmentDate,
        TimeSpan startTime,
        TimeSpan endTime,
        string? note);

    /// <summary>
    /// First-touch email sent to the client right after a public booking is
    /// created and auto-confirmed. Use this instead of
    /// <see cref="NotifyAppointmentStatusChangedAsync"/> for the initial
    /// confirmation so the copy reads as "Termin potvrđen" rather than
    /// "Status promijenjen".
    /// </summary>
    Task NotifyAppointmentConfirmedToClientAsync(
        Guid businessId,
        Guid appointmentId,
        string businessName,
        string? businessEmail,
        string? businessPhone,
        string? businessAddress,
        string clientName,
        string? clientEmail,
        string serviceName,
        DateTime appointmentDate,
        TimeSpan startTime,
        TimeSpan endTime,
        string? cancelUrl);

    /// <summary>
    /// Notifies the client when an existing appointment's status is changed
    /// later (admin rejects, cancels, or marks completed). Do NOT use for the
    /// initial booking — call <see cref="NotifyAppointmentConfirmedToClientAsync"/>
    /// instead.
    /// </summary>
    Task NotifyAppointmentStatusChangedAsync(
        Guid businessId,
        Guid appointmentId,
        AppointmentStatus status,
        string businessName,
        string? businessEmail,
        string? businessPhone,
        string? businessAddress,
        string clientName,
        string? clientEmail,
        string serviceName,
        DateTime appointmentDate,
        TimeSpan startTime,
        TimeSpan endTime);

    /// <summary>
    /// Sends the verification link to a freshly-registered owner. Called by
    /// /api/businesses/register (initial mail) and /api/auth/resend-verification.
    /// Unlike the appointment notifications above, this email is not bound to
    /// an Appointment row, so it is logged with a null AppointmentId.
    /// </summary>
    Task SendEmailVerificationAsync(
        Guid businessId,
        string toEmail,
        string ownerFullName,
        string businessName,
        string verificationUrl);

    /// <summary>
    /// Sends a password-reset link to the owner. The link contains a single-use
    /// token that expires after a short window. Called from
    /// /api/auth/forgot-password. Not bound to an Appointment, so logged with a
    /// null AppointmentId.
    /// </summary>
    Task SendPasswordResetEmailAsync(
        Guid businessId,
        string toEmail,
        string ownerFullName,
        string businessName,
        string resetUrl,
        TimeSpan validFor);

    /// <summary>
    /// Sends a "your appointment is in X" reminder to the client. Called by the
    /// reminder background service, NOT by user actions. Tone matches the
    /// urgency window (24h vs 1h) via the reminderKind parameter.
    /// </summary>
    Task NotifyAppointmentReminderAsync(
        Guid businessId,
        Guid appointmentId,
        AppointmentReminderKind reminderKind,
        string businessName,
        string? businessPhone,
        string? businessAddress,
        string clientName,
        string? clientEmail,
        string serviceName,
        DateTime appointmentDate,
        TimeSpan startTime,
        TimeSpan endTime,
        string? cancelUrl);
}

public enum AppointmentReminderKind
{
    /// <summary>Termin je za ~24 sata.</summary>
    OneDayBefore,
    /// <summary>Termin je za ~1 sat.</summary>
    OneHourBefore
}
