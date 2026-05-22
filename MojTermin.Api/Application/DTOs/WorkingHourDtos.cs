using System.ComponentModel.DataAnnotations;

namespace MojTermin.Api.Application.DTOs;

public class WorkingHourDto
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan OpenTime { get; set; }
    public TimeSpan CloseTime { get; set; }
    public bool IsClosed { get; set; }
}

public class UpdateWorkingHourDto
{
    [Required]
    public DayOfWeek DayOfWeek { get; set; }

    public TimeSpan OpenTime { get; set; }
    public TimeSpan CloseTime { get; set; }
    public bool IsClosed { get; set; }
}
