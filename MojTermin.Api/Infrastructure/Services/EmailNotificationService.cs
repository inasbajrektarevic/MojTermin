using Microsoft.Extensions.Options;
using MojTermin.Api.Application.Interfaces;
using MojTermin.Api.Domain.Entities;
using MojTermin.Api.Domain.Enums;
using MojTermin.Api.Infrastructure.Data;

namespace MojTermin.Api.Infrastructure.Services;

/// <summary>
/// Application-facing notification entrypoint. Handles deterministic decisions
/// (recipient missing, notifications disabled, SMTP misconfigured) synchronously
/// so the admin panel sees the Skipped/Failed log immediately. The actual SMTP
/// send is delegated to <see cref="EmailDispatcherHostedService"/> via
/// <see cref="EmailQueue"/> so a slow SMTP server never blocks an HTTP request.
/// </summary>
public class EmailNotificationService(
    MojTerminDbContext dbContext,
    EmailQueue queue,
    IOptions<NotificationOptions> options,
    ILogger<EmailNotificationService> logger) : INotificationService
{
    private readonly NotificationOptions _options = options.Value;

    public async Task NotifyNewAppointmentRequestAsync(
        Guid businessId,
        Guid appointmentId,
        string businessName,
        string? businessEmail,
        string clientName,
        string clientPhone,
        string? clientEmail,
        string serviceName,
        DateTime appointmentDate,
        TimeSpan startTime,
        TimeSpan endTime,
        string? note)
    {
        var recipient = businessEmail?.Trim();
        if (string.IsNullOrWhiteSpace(recipient))
        {
            logger.LogWarning("Skipping new appointment notification because business email is missing.");
            await LogNotificationAsync(
                businessId,
                appointmentId,
                recipient: "N/A",
                subject: $"Nova rezervacija - {businessName}",
                body: "Business email nije podešen.",
                status: NotificationDeliveryStatus.Skipped,
                errorMessage: "Business email nije dostupan.");
            return;
        }

        var subject = $"Nova rezervacija - {businessName}";
        var body = $"""
            Primljena je nova rezervacija termina.

            Biznis: {businessName}
            Klijent: {clientName}
            Telefon: {clientPhone}
            Email klijenta: {clientEmail ?? "-"}
            Usluga: {serviceName}
            Datum: {appointmentDate:yyyy-MM-dd}
            Vrijeme: {startTime:hh\:mm} - {endTime:hh\:mm}
            Napomena: {note ?? "-"}
            Status: Potvrđen
            """;

        await SendEmailSafelyAsync(
            businessId,
            appointmentId,
            recipient,
            subject,
            body);
    }

    public async Task NotifyAppointmentConfirmedToClientAsync(
        Guid businessId,
        Guid appointmentId,
        string businessName,
        string? businessEmail,
        string? businessPhone,
        string? businessAddress,
        string clientName,
        string? clientEmail,
        string serviceName,
        DateTime appointmentDate,
        TimeSpan startTime,
        TimeSpan endTime,
        string? cancelUrl)
    {
        var recipient = clientEmail?.Trim();
        if (string.IsNullOrWhiteSpace(recipient))
        {
            logger.LogInformation("Skipping client confirmation email because client email is missing.");
            await LogNotificationAsync(
                businessId,
                appointmentId,
                recipient: "N/A",
                subject: $"Termin potvrđen - {businessName}",
                body: "Client email nije dostupan.",
                status: NotificationDeliveryStatus.Skipped,
                errorMessage: "Client email nije dostupan.");
            return;
        }

        var subject = $"Termin potvrđen - {businessName}";

        // Build a longer contact-info block only when we have at least one
        // piece of business contact info — keeps the email tidy for tenants
        // who haven't filled in their profile yet.
        var contactLines = new List<string>();
        if (!string.IsNullOrWhiteSpace(businessPhone))
        {
            contactLines.Add($"Telefon: {businessPhone}");
        }
        if (!string.IsNullOrWhiteSpace(businessEmail))
        {
            contactLines.Add($"Email: {businessEmail}");
        }
        if (!string.IsNullOrWhiteSpace(businessAddress))
        {
            contactLines.Add($"Adresa: {businessAddress}");
        }
        var contactBlock = contactLines.Count > 0
            ? "\nKontakt salonа:\n" + string.Join("\n", contactLines) + "\n"
            : string.Empty;

        // Embed a one-click cancel link when one is supplied. The token in the
        // URL is single-use server-side; if omitted (e.g. caller could not build
        // the URL) the email falls back to the "call the salon" instruction so
        // we never silently drop the cancel affordance.
        var cancelBlock = !string.IsNullOrWhiteSpace(cancelUrl)
            ? $"\nNe možete doći? Otkažite termin jednim klikom:\n{cancelUrl}\n\nLink vrijedi do početka termina.\n"
            : "\nAko trebate izmijeniti ili otkazati termin, kontaktirajte salon putem gore navedenih podataka.\n";

        var body = $"""
            Poštovani/a {clientName},

            Vaš termin je uspješno potvrđen. Detalji rezervacije:

            Biznis: {businessName}
            Usluga: {serviceName}
            Datum: {appointmentDate:yyyy-MM-dd}
            Vrijeme: {startTime:hh\:mm} - {endTime:hh\:mm}
            {contactBlock}{cancelBlock}
            Hvala vam na povjerenju.
            """;

        await SendEmailSafelyAsync(
            businessId,
            appointmentId,
            recipient,
            subject,
            body);
    }

    public async Task NotifyAppointmentStatusChangedAsync(
        Guid businessId,
        Guid appointmentId,
        AppointmentStatus status,
        string businessName,
        string? businessEmail,
        string? businessPhone,
        string? businessAddress,
        string clientName,
        string? clientEmail,
        string serviceName,
        DateTime appointmentDate,
        TimeSpan startTime,
        TimeSpan endTime)
    {
        // Status-specific subject + body intro. Tone matches the status so a
        // cancellation does not read like a routine status nudge.
        var (subjectSuffix, intro, closing) = status switch
        {
            AppointmentStatus.Cancelled => (
                "Termin otkazan",
                "Obavještavamo vas da je vaš termin otkazan.",
                "Ako želite zakazati novi termin, slobodno nas kontaktirajte."),
            AppointmentStatus.Rejected => (
                "Termin odbijen",
                "Nažalost, vaš zahtjev za termin je odbijen.",
                "Možete pokušati zakazati drugi termin ili nas kontaktirati za više informacija."),
            AppointmentStatus.Completed => (
                "Termin završen",
                "Vaš termin je uspješno završen. Hvala što ste nas posjetili!",
                "Radujemo se ponovnom susretu."),
            AppointmentStatus.Confirmed => (
                "Termin potvrđen",
                "Vaš termin je potvrđen.",
                "Hvala vam na povjerenju."),
            AppointmentStatus.Pending => (
                "Termin na čekanju",
                "Vaš termin je trenutno na čekanju.",
                "Obavijestit ćemo vas o konačnom statusu u najkraćem roku."),
            _ => (
                "Ažuriranje termina",
                "Status vašeg termina je promijenjen.",
                "Hvala vam na povjerenju.")
        };

        var recipient = clientEmail?.Trim();
        if (string.IsNullOrWhiteSpace(recipient))
        {
            logger.LogInformation("Skipping client status email because client email is missing.");
            await LogNotificationAsync(
                businessId,
                appointmentId,
                recipient: "N/A",
                subject: $"{subjectSuffix} - {businessName}",
                body: "Client email nije dostupan.",
                status: NotificationDeliveryStatus.Skipped,
                errorMessage: "Client email nije dostupan.");
            return;
        }

        // Show salon contact info only when at least one piece is present —
        // keeps the email tidy for tenants who haven't completed their profile.
        var contactLines = new List<string>();
        if (!string.IsNullOrWhiteSpace(businessPhone))
        {
            contactLines.Add($"Telefon: {businessPhone}");
        }
        if (!string.IsNullOrWhiteSpace(businessEmail))
        {
            contactLines.Add($"Email: {businessEmail}");
        }
        if (!string.IsNullOrWhiteSpace(businessAddress))
        {
            contactLines.Add($"Adresa: {businessAddress}");
        }
        var contactBlock = contactLines.Count > 0
            ? "\nKontakt salona:\n" + string.Join("\n", contactLines) + "\n"
            : string.Empty;

        var subject = $"{subjectSuffix} - {businessName}";
        var body = $"""
            Poštovani/a {clientName},

            {intro}

            Biznis: {businessName}
            Usluga: {serviceName}
            Datum: {appointmentDate:yyyy-MM-dd}
            Vrijeme: {startTime:hh\:mm} - {endTime:hh\:mm}
            {contactBlock}
            {closing}
            """;

        await SendEmailSafelyAsync(
            businessId,
            appointmentId,
            recipient,
            subject,
            body);
    }

    public async Task SendEmailVerificationAsync(
        Guid businessId,
        string toEmail,
        string ownerFullName,
        string businessName,
        string verificationUrl)
    {
        var recipient = toEmail?.Trim();
        if (string.IsNullOrWhiteSpace(recipient))
        {
            logger.LogWarning("Skipping email verification because owner email is missing for business {BusinessId}.", businessId);
            return;
        }

        var subject = $"Potvrdi svoj email - {businessName}";
        var body = $"""
            Poštovani/a {ownerFullName},

            Hvala što ste registrovali "{businessName}" na MojTermin platformi.

            Za nastavak korištenja admin panela potrebno je da potvrdite svoju email adresu.
            Kliknite na link ispod (vrijedi 24 sata):

            {verificationUrl}

            Ako niste vi pokrenuli registraciju, slobodno zanemarite ovaj email.

            Hvala vam,
            MojTermin tim
            """;

        await SendEmailSafelyAsync(
            businessId,
            appointmentId: null,
            recipient,
            subject,
            body);
    }

    public async Task NotifyAppointmentReminderAsync(
        Guid businessId,
        Guid appointmentId,
        AppointmentReminderKind reminderKind,
        string businessName,
        string? businessPhone,
        string? businessAddress,
        string clientName,
        string? clientEmail,
        string serviceName,
        DateTime appointmentDate,
        TimeSpan startTime,
        TimeSpan endTime,
        string? cancelUrl)
    {
        var recipient = clientEmail?.Trim();
        if (string.IsNullOrWhiteSpace(recipient))
        {
            logger.LogInformation(
                "Skipping reminder email for appointment {AppointmentId} because client email is missing.",
                appointmentId);
            await LogNotificationAsync(
                businessId,
                appointmentId,
                recipient: "N/A",
                subject: reminderKind == AppointmentReminderKind.OneDayBefore
                    ? $"Podsjetnik: termin sutra - {businessName}"
                    : $"Podsjetnik: termin za 1 sat - {businessName}",
                body: "Client email nije dostupan.",
                status: NotificationDeliveryStatus.Skipped,
                errorMessage: "Client email nije dostupan.");
            return;
        }

        var contactLines = new List<string>();
        if (!string.IsNullOrWhiteSpace(businessPhone))
        {
            contactLines.Add($"Telefon: {businessPhone}");
        }
        if (!string.IsNullOrWhiteSpace(businessAddress))
        {
            contactLines.Add($"Adresa: {businessAddress}");
        }
        var contactBlock = contactLines.Count > 0
            ? "\nKontakt salona:\n" + string.Join("\n", contactLines) + "\n"
            : string.Empty;

        var cancelBlock = !string.IsNullOrWhiteSpace(cancelUrl)
            ? $"\nNe možete doći? Otkažite jednim klikom:\n{cancelUrl}\n"
            : string.Empty;

        var (subject, intro) = reminderKind switch
        {
            AppointmentReminderKind.OneDayBefore => (
                $"Podsjetnik: termin sutra - {businessName}",
                "Podsjećamo vas da imate termin SUTRA."),
            AppointmentReminderKind.OneHourBefore => (
                $"Podsjetnik: termin za 1 sat - {businessName}",
                "Podsjećamo vas da imate termin za otprilike 1 sat."),
            _ => (
                $"Podsjetnik za termin - {businessName}",
                "Podsjećamo vas da imate predstojeći termin.")
        };

        var body = $"""
            Poštovani/a {clientName},

            {intro}

            Biznis: {businessName}
            Usluga: {serviceName}
            Datum: {appointmentDate:yyyy-MM-dd}
            Vrijeme: {startTime:hh\:mm} - {endTime:hh\:mm}
            {contactBlock}{cancelBlock}
            Vidimo se uskoro.
            """;

        await SendEmailSafelyAsync(
            businessId,
            appointmentId,
            recipient,
            subject,
            body);
    }

    public async Task SendPasswordResetEmailAsync(
        Guid businessId,
        string toEmail,
        string ownerFullName,
        string businessName,
        string resetUrl,
        TimeSpan validFor)
    {
        var recipient = toEmail?.Trim();
        if (string.IsNullOrWhiteSpace(recipient))
        {
            logger.LogWarning("Skipping password reset email because owner email is missing for business {BusinessId}.", businessId);
            return;
        }

        var minutes = (int)Math.Max(1, Math.Round(validFor.TotalMinutes));
        var humanWindow = minutes >= 60
            ? $"{minutes / 60} sat{(minutes / 60 == 1 ? "" : "a")}"
            : $"{minutes} minut{(minutes == 1 ? "" : "a")}";

        var subject = $"Resetovanje lozinke - {businessName}";
        var body = $"""
            Poštovani/a {ownerFullName},

            Primili smo zahtjev za resetovanje lozinke na vašem nalogu za "{businessName}".

            Kliknite na link ispod da postavite novu lozinku (link vrijedi {humanWindow}):

            {resetUrl}

            Ako niste vi tražili reset, slobodno zanemarite ovaj email — vaša lozinka ostaje nepromijenjena. Iz sigurnosnih razloga preporučujemo da provjerite ko ima pristup vašoj email adresi.

            Hvala vam,
            MojTermin tim
            """;

        await SendEmailSafelyAsync(
            businessId,
            appointmentId: null,
            recipient,
            subject,
            body);
    }

    private async Task SendEmailSafelyAsync(
        Guid businessId,
        Guid? appointmentId,
        string to,
        string subject,
        string body)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Notifications disabled. Would send email to {Email} with subject {Subject}.", to, subject);
            await LogNotificationAsync(
                businessId,
                appointmentId,
                to,
                subject,
                body,
                NotificationDeliveryStatus.Skipped,
                "Notifications disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.SenderEmail) ||
            string.IsNullOrWhiteSpace(_options.SmtpHost) ||
            string.IsNullOrWhiteSpace(_options.SmtpUsername) ||
            string.IsNullOrWhiteSpace(_options.SmtpPassword))
        {
            logger.LogError("Notifications are enabled but SMTP configuration is incomplete.");
            await LogNotificationAsync(
                businessId,
                appointmentId,
                to,
                subject,
                body,
                NotificationDeliveryStatus.Failed,
                "SMTP konfiguracija nije kompletna.");
            return;
        }

        // Hand off the actual SMTP work to the background dispatcher. The Sent/Failed
        // log entry will be written by the dispatcher in its own DI scope shortly after.
        await queue.EnqueueAsync(new EmailWorkItem(businessId, appointmentId, to, subject, body));
    }

    private async Task LogNotificationAsync(
        Guid businessId,
        Guid? appointmentId,
        string recipient,
        string subject,
        string body,
        NotificationDeliveryStatus status,
        string? errorMessage,
        DateTime? sentAtUtc = null)
    {
        var entry = new NotificationLog
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            AppointmentId = appointmentId,
            Channel = NotificationChannel.Email,
            Status = status,
            Recipient = recipient,
            Subject = subject,
            BodyPreview = body.Length > 1200 ? body[..1200] : body,
            ErrorMessage = errorMessage,
            CreatedAtUtc = DateTime.UtcNow,
            SentAtUtc = sentAtUtc
        };

        dbContext.NotificationLogs.Add(entry);
        await dbContext.SaveChangesAsync();
    }
}
