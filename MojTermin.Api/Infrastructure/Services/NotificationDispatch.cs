namespace MojTermin.Api.Infrastructure.Services;

public static class NotificationDispatch
{
    public static bool IsConfigured(NotificationOptions options) =>
        options.Enabled &&
        !string.IsNullOrWhiteSpace(options.SenderEmail) &&
        !string.IsNullOrWhiteSpace(options.SmtpHost) &&
        !string.IsNullOrWhiteSpace(options.SmtpUsername) &&
        !string.IsNullOrWhiteSpace(options.SmtpPassword);
}
