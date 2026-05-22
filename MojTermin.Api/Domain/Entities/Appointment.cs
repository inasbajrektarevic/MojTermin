using MojTermin.Api.Domain.Enums;

namespace MojTermin.Api.Domain.Entities;

public class Appointment
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ServiceId { get; set; }
    public Guid ClientId { get; set; }
    public Guid? StaffMemberId { get; set; }
    public DateTime AppointmentDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;
    public string? Note { get; set; }
    /// <summary>
    /// Podaci uneseni pri ovoj rezervaciji. Koriste se za potvrdu i podsjetnike
    /// čak i kad CRM klijent (po telefonu) ima drugačije ime ili email.
    /// </summary>
    public string? ContactFullName { get; set; }
    public string? ContactEmail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Concurrency token. SQL Server stamps a new value on every UPDATE; if two
    // admins (or an admin and the auto-complete background job) try to modify
    // the same row, the second write throws DbUpdateConcurrencyException
    // instead of silently overwriting the first.
    public byte[]? RowVersion { get; set; }

    // Client self-service cancellation:
    //  - CancellationTokenHash is the SHA-256 of a random token embedded in the
    //    confirmation email. Hash, not raw value, so an exfiltrated DB cannot be
    //    replayed against /api/public/appointments/cancel.
    //  - CancellationTokenExpiresAtUtc is set to the appointment start time, so
    //    the link stops working the moment the appointment begins. We do NOT
    //    let clients cancel after that — they need to call the salon.
    public string? CancellationTokenHash { get; set; }
    public DateTime? CancellationTokenExpiresAtUtc { get; set; }

    // Reminder dispatcher state: idempotency flags so the background job that
    // sweeps for upcoming appointments never sends the same reminder twice.
    // Wrapped in nullable DateTime instead of bool so we can also tell ops
    // "when was the reminder enqueued" without a separate audit table.
    public DateTime? Reminder24hSentAtUtc { get; set; }
    public DateTime? Reminder1hSentAtUtc { get; set; }

    public Business? Business { get; set; }
    public Service? Service { get; set; }
    public Client? Client { get; set; }
    public StaffMember? StaffMember { get; set; }
}
