namespace MojTermin.Api.Infrastructure.Services;

public class NotificationOptions
{
    public const string SectionName = "Notifications";

    public bool Enabled { get; set; } = false;
    public string SenderName { get; set; } = "MojTermin";
    public string SenderEmail { get; set; } = string.Empty;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string SmtpUsername { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
}
