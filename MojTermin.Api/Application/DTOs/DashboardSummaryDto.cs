namespace MojTermin.Api.Application.DTOs;

public class DashboardSummaryDto
{
    public int TodaysAppointments { get; set; }
    public int ThisWeekAppointments { get; set; }
    public int NewClients { get; set; }
    public int PendingAppointments { get; set; }
}
