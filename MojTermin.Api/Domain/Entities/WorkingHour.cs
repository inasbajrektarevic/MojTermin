namespace MojTermin.Api.Domain.Entities;

public class WorkingHour
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan OpenTime { get; set; }
    public TimeSpan CloseTime { get; set; }
    public bool IsClosed { get; set; }

    public Business? Business { get; set; }
}
