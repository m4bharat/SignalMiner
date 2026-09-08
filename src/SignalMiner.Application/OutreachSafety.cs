using System.Net.Mail;
using SignalMiner.Domain;

namespace SignalMiner.Application;

public static class EmailAddress
{
    public static string Normalize(string value)
    {
        var email = value.Trim().ToLowerInvariant();
        if (email.Contains('\r') || email.Contains('\n') || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            throw new ManualEmailException("A valid email address is required.");
        return email;
    }
}

public sealed record OutreachPolicy(int DailySendLimit = 50, int DelayBetweenMessagesSeconds = 10)
{
    public void Validate()
    {
        if (DailySendLimit is < 1 or > 10000 || DelayBetweenMessagesSeconds is < 1 or > 3600)
            throw new InvalidOperationException("Email:Outreach requires DailySendLimit 1–10000 and DelayBetweenMessagesSeconds 1–3600.");
    }
}

public sealed record OutreachCapacity(int DailySendLimit, int DelayBetweenMessagesSeconds, int RemainingCapacity);
public interface IOutreachSafety
{
    Task<IOutreachSendLease> BeginAsync(Guid leadId, IReadOnlyList<string> recipients, CancellationToken cancellationToken);
    Task<OutreachCapacity> CapacityAsync(CancellationToken cancellationToken);
}
public interface IOutreachSendLease : IAsyncDisposable
{
    Task SubmittedAsync(EmailDeliveryResult result);
    Task UncertainAsync(string details);
}
public sealed record SuppressionRequest(SuppressionReason Reason, string? Details = null);
public sealed record SuppressionSearchResult(IReadOnlyList<EmailSuppression> Items, int Total);
public interface ISuppressionService
{
    Task<EmailSuppression?> RecordAsync(Guid leadId, SuppressionRequest request, CancellationToken cancellationToken);
    Task<SuppressionSearchResult> SearchAsync(string? query, int page, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, EmailSuppression>> FindAsync(IEnumerable<string> emails, CancellationToken cancellationToken);
}
