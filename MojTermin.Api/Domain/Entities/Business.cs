using MojTermin.Api.Domain.Enums;

namespace MojTermin.Api.Domain.Entities;

public class Business
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public BusinessType BusinessType { get; set; } = BusinessType.Other;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? CoverImageUrl { get; set; }
    public string ThemePreset { get; set; } = "default";
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Service> Services { get; set; } = new List<Service>();
    public ICollection<WorkingHour> WorkingHours { get; set; } = new List<WorkingHour>();
    public ICollection<Client> Clients { get; set; } = new List<Client>();
    public ICollection<StaffMember> StaffMembers { get; set; } = new List<StaffMember>();
    public ICollection<StaffTimeOff> StaffTimeOffs { get; set; } = new List<StaffTimeOff>();
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public ICollection<NotificationLog> NotificationLogs { get; set; } = new List<NotificationLog>();
    public ICollection<AdminAuditLog> AdminAuditLogs { get; set; } = new List<AdminAuditLog>();
}
