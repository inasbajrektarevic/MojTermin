namespace MojTermin.Api.Domain.Entities;

public class Client
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Business? Business { get; set; }
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}
