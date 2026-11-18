using System.Text.Json;
using BajkoweCuda.Application.Emails;
using BajkoweCuda.Infrastructure.Persistence;

namespace BajkoweCuda.Infrastructure.Emails;

internal sealed class EmailOutbox(AppDbContext db, TimeProvider time, EmailDispatchSignal signal) : IEmailOutbox
{
    public async Task EnqueueAsync(
        EmailTemplate emailTemplate,
        Guid? toUserId,
        string toEmail,
        string language,
        IReadOnlyDictionary<string, string> payload,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        db.EmailOutbox.Add(new EmailOutboxMessage
        {
            Id = Guid.CreateVersion7(now),
            Kind = EmailKind.Transactional,
            ToUserId = toUserId,
            ToEmail = toEmail,
            Language = language,
            Template = emailTemplate.ToString(),
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAt = now,
            SendAfter = now,
            Status = EmailStatus.Pending,
        });

        await db.SaveChangesAsync(cancellationToken);
        signal.Notify();
    }
}
