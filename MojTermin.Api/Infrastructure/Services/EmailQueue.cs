using System.Threading.Channels;

namespace MojTermin.Api.Infrastructure.Services;

public sealed record EmailWorkItem(
    Guid BusinessId,
    // Nullable: appointment-scoped notifications carry the appointment id, but
    // owner-account emails (e.g. email verification) have no appointment to link
    // and would fail the FK constraint on NotificationLogs if forced to a value.
    Guid? AppointmentId,
    string Recipient,
    string Subject,
    string Body);

/// <summary>
/// In-process work queue for outbound email. Producers (request scope) write
/// items; <see cref="EmailDispatcherHostedService"/> drains them in the
/// background so an SMTP timeout does not stall an HTTP response.
///
/// Bounded so a runaway loop or burst of bookings cannot grow the channel
/// indefinitely; once full, producers wait (back-pressure) instead of dropping
/// notifications.
/// </summary>
public sealed class EmailQueue
{
    private readonly Channel<EmailWorkItem> _channel;

    public EmailQueue()
    {
        _channel = Channel.CreateBounded<EmailWorkItem>(new BoundedChannelOptions(capacity: 1024)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ValueTask EnqueueAsync(EmailWorkItem item, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<EmailWorkItem> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
