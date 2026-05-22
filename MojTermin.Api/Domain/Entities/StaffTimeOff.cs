namespace MojTermin.Api.Domain.Entities;

public class StaffTimeOff
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid StaffMemberId { get; set; }
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public TimeSpan? TimeFrom { get; set; }
    public TimeSpan? TimeTo { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Business? Business { get; set; }
    public StaffMember? StaffMember { get; set; }
}
