using MojTermin.Api.Domain.Enums;

namespace MojTermin.Api.Domain.Entities;

public class NotificationLog
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid? AppointmentId { get; set; }
    public NotificationChannel Channel { get; set; } = NotificationChannel.Email;
    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Skipped;
    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyPreview { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }

    public Business? Business { get; set; }
    public Appointment? Appointment { get; set; }
}
