using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using MojTermin.Api.Domain.Entities;
using MojTermin.Api.Domain.Enums;
using MojTermin.Api.Infrastructure.Data;

namespace MojTermin.Api.Infrastructure.Services;

/// <summary>
/// Drains the <see cref="EmailQueue"/> and performs the actual SMTP send in the
/// background. Failures are recorded in NotificationLogs so the admin panel can
/// surface them, but they do NOT propagate to the original HTTP request that
/// triggered the notification.
/// </summary>
public class EmailDispatcherHostedService(
    EmailQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationOptions> options,
    ILogger<EmailDispatcherHostedService> logger) : BackgroundService
{
    private readonly NotificationOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(item, stoppingToken);
            }
            catch (Exception ex)
            {
                // Defensive: a single bad item must never tear down the dispatcher.
                logger.LogError(ex,
                    "Email dispatcher unexpectedly failed for appointment {AppointmentId}.",
                    item.AppointmentId);
            }
        }
    }

    private async Task ProcessAsync(EmailWorkItem item, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MojTerminDbContext>();

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_options.SenderEmail, _options.SenderName),
                Subject = item.Subject,
                Body = item.Body
            };
            message.To.Add(item.Recipient);

            using var smtp = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
            {
                EnableSsl = _options.UseSsl,
                Credentials = new NetworkCredential(_options.SmtpUsername, _options.SmtpPassword)
            };

            await smtp.SendMailAsync(message, cancellationToken);
            await WriteLogAsync(dbContext, item, NotificationDeliveryStatus.Sent, null, DateTime.UtcNow, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to send email notification to {Recipient} for appointment {AppointmentId}.",
                item.Recipient, item.AppointmentId);
            await WriteLogAsync(dbContext, item, NotificationDeliveryStatus.Failed, ex.Message, null, cancellationToken);
        }
    }

    private static async Task WriteLogAsync(
        MojTerminDbContext dbContext,
        EmailWorkItem item,
        NotificationDeliveryStatus status,
        string? errorMessage,
        DateTime? sentAtUtc,
        CancellationToken cancellationToken)
    {
        var entry = new NotificationLog
        {
            Id = Guid.NewGuid(),
            BusinessId = item.BusinessId,
            AppointmentId = item.AppointmentId,
            Channel = NotificationChannel.Email,
            Status = status,
            Recipient = item.Recipient,
            Subject = item.Subject,
            BodyPreview = item.Body.Length > 1200 ? item.Body[..1200] : item.Body,
            ErrorMessage = errorMessage,
            CreatedAtUtc = DateTime.UtcNow,
            SentAtUtc = sentAtUtc
        };

        dbContext.NotificationLogs.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
