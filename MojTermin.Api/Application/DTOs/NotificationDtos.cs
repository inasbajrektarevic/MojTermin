using MojTermin.Api.Domain.Enums;

namespace MojTermin.Api.Application.DTOs;

public class NotificationLogDto
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid? AppointmentId { get; set; }
    public NotificationChannel Channel { get; set; }
    public NotificationDeliveryStatus Status { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyPreview { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
}
