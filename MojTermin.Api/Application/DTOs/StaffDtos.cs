using System.ComponentModel.DataAnnotations;

namespace MojTermin.Api.Application.DTOs;

public class StaffMemberDto
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateStaffMemberDto
{
    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? Title { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(120), EmailAddress]
    public string? Email { get; set; }
}

public class UpdateStaffMemberDto : CreateStaffMemberDto
{
    public bool IsActive { get; set; } = true;
}

public class StaffTimeOffDto
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid StaffMemberId { get; set; }
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public TimeSpan? TimeFrom { get; set; }
    public TimeSpan? TimeTo { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class CreateStaffTimeOffDto
{
    [Required]
    public DateTime DateFrom { get; set; }

    [Required]
    public DateTime DateTo { get; set; }

    public TimeSpan? TimeFrom { get; set; }
    public TimeSpan? TimeTo { get; set; }

    [MaxLength(300)]
    public string? Reason { get; set; }
}
