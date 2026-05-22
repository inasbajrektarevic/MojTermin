using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MojTermin.Api.Application.DTOs;
using MojTermin.Api.Application.Interfaces;
using MojTermin.Api.Domain.Enums;
using MojTermin.Api.Infrastructure.Data;
using System.Globalization;
using System.Text;

namespace MojTermin.Api.API.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = "OwnerOnly")]
public class DashboardController(
    MojTerminDbContext dbContext,
    ICurrentBusinessService currentBusinessService,
    IAdminAuditService adminAuditService) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary()
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var now = DateTime.UtcNow;
        var today = now.Date;
        var weekStart = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
        if (today.DayOfWeek == DayOfWeek.Sunday)
        {
            weekStart = today.AddDays(-6);
        }

        var weekEnd = weekStart.AddDays(7);
        var sevenDaysAgo = today.AddDays(-7);

        var summary = new DashboardSummaryDto
        {
            TodaysAppointments = await dbContext.Appointments
                .CountAsync(x => x.BusinessId == businessId && x.AppointmentDate.Date == today),
            ThisWeekAppointments = await dbContext.Appointments
                .CountAsync(x => x.BusinessId == businessId && x.AppointmentDate.Date >= weekStart && x.AppointmentDate.Date < weekEnd),
            NewClients = await dbContext.Clients
                .CountAsync(x => x.BusinessId == businessId && x.CreatedAt >= sevenDaysAgo),
            PendingAppointments = await dbContext.Appointments
                .CountAsync(x => x.BusinessId == businessId && x.Status == AppointmentStatus.Pending)
        };

        return Ok(summary);
    }

    [HttpGet("revenue/export.csv")]
    public async Task<IActionResult> ExportRevenueCsv(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var businessId = currentBusinessService.GetRequiredBusinessId();
        var fromDate = (from ?? DateTime.UtcNow.Date.AddDays(-30)).Date;
        var toDate = (to ?? DateTime.UtcNow.Date).Date;

        if (toDate < fromDate)
        {
            return BadRequest("Parametar 'to' mora biti isti ili nakon 'from'.");
        }

        var rows = await dbContext.Appointments
            .Where(x =>
                x.BusinessId == businessId &&
                x.Status == AppointmentStatus.Completed &&
                x.AppointmentDate.Date >= fromDate &&
                x.AppointmentDate.Date <= toDate)
            .Include(x => x.Service)
            .Include(x => x.Client)
            .OrderBy(x => x.AppointmentDate)
            .ThenBy(x => x.StartTime)
            .Select(x => new RevenueExportRow(
                x.AppointmentDate,
                x.StartTime,
                x.Service != null ? x.Service.Name : "N/A",
                x.Client != null ? x.Client.FullName : "N/A",
                x.Service != null ? x.Service.Price : 0m))
            .ToListAsync();

        var total = rows.Sum(x => x.Price);
        var csv = BuildRevenueCsv(rows, fromDate, toDate, total);
        var bytes = Encoding.UTF8.GetBytes(csv);
        var fileName = $"prihod-{fromDate:yyyyMMdd}-{toDate:yyyyMMdd}.csv";
        await adminAuditService.LogAsync(
            businessId,
            action: "export",
            resourceType: "revenue",
            summary: $"Izvezen revenue CSV ({fromDate:yyyy-MM-dd} - {toDate:yyyy-MM-dd})",
            metadata: new { Rows = rows.Count, Total = total });
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private sealed record RevenueExportRow(
        DateTime AppointmentDate,
        TimeSpan StartTime,
        string ServiceName,
        string ClientName,
        decimal Price);

    private static string BuildRevenueCsv(
        IEnumerable<RevenueExportRow> rows,
        DateTime fromDate,
        DateTime toDate,
        decimal total)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"\"Period od\",\"{fromDate:yyyy-MM-dd}\"");
        sb.AppendLine($"\"Period do\",\"{toDate:yyyy-MM-dd}\"");
        sb.AppendLine();
        sb.AppendLine("Datum,Vrijeme,Usluga,Klijent,Cijena");

        foreach (var row in rows)
        {
            sb.Append($"\"{row.AppointmentDate:yyyy-MM-dd}\",");
            sb.Append($"\"{row.StartTime:hh\\:mm}\",");
            sb.Append($"{EscapeCsv(row.ServiceName)},");
            sb.Append($"{EscapeCsv(row.ClientName)},");
            sb.Append($"\"{row.Price.ToString("0.00", CultureInfo.InvariantCulture)}\"");
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine($"\"UKUPNO\",,,,\"{total.ToString("0.00", CultureInfo.InvariantCulture)}\"");
        return sb.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"\"";
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
