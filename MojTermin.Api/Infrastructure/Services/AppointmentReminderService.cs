using Microsoft.EntityFrameworkCore;
using MojTermin.Api.Application.Interfaces;
using MojTermin.Api.Domain.Entities;
using MojTermin.Api.Domain.Enums;
using MojTermin.Api.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace MojTermin.Api.Infrastructure.Services;

/// <summary>
/// Background sweeper that mails clients a "your appointment is tomorrow" and
/// "your appointment is in 1 hour" reminder. Idempotent via two timestamp
/// columns on Appointment so a restart in the middle of a sweep cannot
/// double-send.
///
/// Why a background service instead of cron / hangfire?
///   - We already host the API as a long-running process, so adding cron-style
///     infra would be overkill for a couple of reminder windows.
///   - The poll cadence is short enough (5 min) that a single missed tick
///     still hits the 24h window comfortably (look-back is 30 minutes wide).
/// </summary>
public class AppointmentReminderService(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationOptions> notificationOptions,
    ILogger<AppointmentReminderService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How wide a window we accept around the ideal reminder time. Wider than
    /// PollInterval so a single skipped/late tick still catches the appointment.
    /// </summary>
    private static readonly TimeSpan ReminderWindowSlack = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Don't burn DB/SMTP when notifications are globally disabled (e.g. in
        // local dev or during integration tests).
        if (!notificationOptions.Value.Enabled)
        {
            logger.LogInformation("Notifications are disabled; AppointmentReminderService will idle.");
            // Still loop so toggling the config at runtime (rare) would be picked
            // up next sweep, but with a longer delay to avoid wasted wakeups.
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
                if (notificationOptions.Value.Enabled)
                {
                    break;
                }
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AppointmentReminderService sweep failed.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MojTerminDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var clientAppOptions = scope.ServiceProvider.GetRequiredService<IOptions<ClientAppOptions>>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<BusinessTimeProvider>();

        var nowLocal = timeProvider.LocalNow;

        // 24h window: appointment is between [23.5h, 24.5h] from now.
        var oneDayMin = nowLocal.AddHours(24).Subtract(ReminderWindowSlack);
        var oneDayMax = nowLocal.AddHours(24).Add(ReminderWindowSlack);
        await SendRemindersForWindowAsync(
            dbContext,
            notifier,
            clientAppOptions.Value,
            AppointmentReminderKind.OneDayBefore,
            oneDayMin,
            oneDayMax,
            cancellationToken);

        // 1h window: appointment is between [30min, 90min] from now.
        var oneHourMin = nowLocal.AddMinutes(60).Subtract(ReminderWindowSlack);
        var oneHourMax = nowLocal.AddMinutes(60).Add(ReminderWindowSlack);
        await SendRemindersForWindowAsync(
            dbContext,
            notifier,
            clientAppOptions.Value,
            AppointmentReminderKind.OneHourBefore,
            oneHourMin,
            oneHourMax,
            cancellationToken);
    }

    private async Task SendRemindersForWindowAsync(
        MojTerminDbContext dbContext,
        INotificationService notifier,
        ClientAppOptions clientAppOptions,
        AppointmentReminderKind kind,
        DateTime windowStartLocal,
        DateTime windowEndLocal,
        CancellationToken cancellationToken)
    {
        // SQL Server can't combine date + TimeSpan directly, so we filter on
        // AppointmentDate first (cheap, indexed) and then compute the exact
        // start datetime in memory.
        var dateStart = windowStartLocal.Date.AddDays(-1);
        var dateEnd = windowEndLocal.Date.AddDays(1);

        var candidates = await dbContext.Appointments
            .Include(x => x.Business)
            .Include(x => x.Service)
            .Include(x => x.Client)
            .Where(x =>
                x.Status == AppointmentStatus.Confirmed &&
                x.AppointmentDate >= dateStart &&
                x.AppointmentDate <= dateEnd)
            .Where(x => kind == AppointmentReminderKind.OneDayBefore
                ? x.Reminder24hSentAtUtc == null
                : x.Reminder1hSentAtUtc == null)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var appointment in candidates)
        {
            var startLocal = appointment.AppointmentDate.Date.Add(appointment.StartTime);
            if (startLocal < windowStartLocal || startLocal > windowEndLocal)
            {
                continue;
            }

            if (appointment.Business is null || appointment.Service is null || appointment.Client is null)
            {
                // Defensive: include() should always populate these. Skip rather
                // than NRE if they ever come back null.
                continue;
            }

            string? cancelUrl = null;
            if (!string.IsNullOrWhiteSpace(appointment.CancellationTokenHash))
            {
                // We hashed the token at booking and cannot recover the raw value
                // for the email, so reminders only include the cancel link when
                // the original confirmation email also did. After the appointment
                // start the token is invalid anyway.
                cancelUrl = null;
            }

            // Mark BEFORE sending so a transient SMTP failure doesn't loop us
            // back into the same row every 5 minutes. The notification log will
            // still capture Sent/Failed status for ops visibility.
            var nowUtc = DateTime.UtcNow;
            if (kind == AppointmentReminderKind.OneDayBefore)
            {
                appointment.Reminder24hSentAtUtc = nowUtc;
            }
            else
            {
                appointment.Reminder1hSentAtUtc = nowUtc;
            }

            try
            {
                await notifier.NotifyAppointmentReminderAsync(
                    appointment.BusinessId,
                    appointment.Id,
                    kind,
                    appointment.Business.Name,
                    appointment.Business.Phone,
                    appointment.Business.Address,
                    !string.IsNullOrWhiteSpace(appointment.ContactFullName)
                        ? appointment.ContactFullName.Trim()
                        : appointment.Client.FullName,
                    !string.IsNullOrWhiteSpace(appointment.ContactEmail)
                        ? appointment.ContactEmail.Trim()
                        : appointment.Client.Email,
                    appointment.Service.Name,
                    appointment.AppointmentDate,
                    appointment.StartTime,
                    appointment.EndTime,
                    cancelUrl);

                sent++;
            }
            catch (Exception ex)
            {
                // Don't unmark — if SMTP is broken right now we'd just spam the
                // log on every poll. Ops should see the error and either fix
                // SMTP or manually clear the flag.
                logger.LogError(ex,
                    "Failed to send {Kind} reminder for appointment {AppointmentId}.",
                    kind,
                    appointment.Id);
            }
        }

        if (candidates.Count > 0)
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                if (sent > 0)
                {
                    logger.LogInformation(
                        "AppointmentReminderService: sent {Sent} {Kind} reminder(s).",
                        sent,
                        kind);
                }
            }
            catch (DbUpdateConcurrencyException ex)
            {
                logger.LogDebug(ex,
                    "Reminder service hit a concurrency conflict; will retry next sweep.");
            }
        }
    }
}
