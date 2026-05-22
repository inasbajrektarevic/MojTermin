using Microsoft.EntityFrameworkCore;
using MojTermin.Api.Domain.Enums;
using MojTermin.Api.Infrastructure.Data;

namespace MojTermin.Api.Infrastructure.Services;

public class AppointmentAutoCompleteService(
    IServiceScopeFactory scopeFactory,
    ILogger<AppointmentAutoCompleteService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CompleteExpiredAppointmentsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Greška u auto-complete servisu za termine.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task CompleteExpiredAppointmentsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MojTerminDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<BusinessTimeProvider>();

        // Appointments are stored as business wall-clock time (e.g. Europe/Sarajevo),
        // so we must compare against the business' local "now" — NOT DateTime.Now,
        // which is UTC inside Linux containers and would auto-complete bookings
        // 1-2 hours early during CET/CEST.
        var nowLocal = timeProvider.LocalNow;
        var today = nowLocal.Date;
        var currentTime = nowLocal.TimeOfDay;

        var candidates = await dbContext.Appointments
            .Where(x =>
                x.Status == AppointmentStatus.Confirmed &&
                (x.AppointmentDate.Date < today ||
                 (x.AppointmentDate.Date == today && x.EndTime <= currentTime)))
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return;
        }

        foreach (var appointment in candidates)
        {
            appointment.Status = AppointmentStatus.Completed;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Auto-complete: označeno {Count} termina kao završeno.", candidates.Count);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Race with an admin (e.g. they cancelled a row between our load and save).
            // Not a failure mode that needs alerting — just retry on the next poll.
            logger.LogDebug(ex,
                "Auto-complete encountered a concurrency conflict; will retry on next interval.");
        }
    }
}
