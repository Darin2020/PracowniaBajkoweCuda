using System.Collections.Concurrent;
using BajkoweCuda.Infrastructure.Emails;

namespace BajkoweCuda.Api.IntegrationTests.Emails;

/// <summary>Replaces SMTP/ACS in tests: keeps every email the dispatcher sends.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<RenderedEmail> _sent = new();

    public IReadOnlyCollection<RenderedEmail> Sent => _sent;

    public Task SendAsync(RenderedEmail email, CancellationToken cancellationToken)
    {
        _sent.Enqueue(email);
        return Task.CompletedTask;
    }

    /// <summary>Waits until an email to <paramref name="to"/> with <paramref name="subject"/> arrives (the dispatcher runs in the background).</summary>
    public async Task<RenderedEmail> WaitForAsync(string to, string subject, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTimeOffset.UtcNow < deadline)
        {
            var email = _sent.LastOrDefault(sent => sent.To == to && sent.Subject == subject);
            if (email is not null)
            {
                return email;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"No email '{subject}' to {to}. Sent: {string.Join(", ", _sent.Select(sent => $"{sent.Subject} -> {sent.To}"))}");
    }

    public int Count(string to, string subject) => _sent.Count(sent => sent.To == to && sent.Subject == subject);
}
