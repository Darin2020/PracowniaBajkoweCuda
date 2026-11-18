using System.Threading.Channels;

namespace BajkoweCuda.Infrastructure.Emails;

/// <summary>
/// Wakes the dispatcher when an email is queued. The dispatcher does not poll the database on a timer: production
/// runs one replica all the time, and a timer would keep the serverless database from ever pausing
/// (docs/DECISIONS.md, 2026-11-18).
/// </summary>
public sealed class EmailDispatchSignal
{
    private readonly Channel<bool> _channel =
        Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify() => _channel.Writer.TryWrite(true);

    /// <summary>Waits until <see cref="Notify"/> is called or <paramref name="timeout"/> passes.</summary>
    public async Task WaitAsync(TimeSpan timeout, TimeProvider time, CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            await _channel.Reader.ReadAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The timeout passed: time for a scheduled send or retry.
        }
    }
}
