using Microsoft.EntityFrameworkCore;
using SignalMiner.Application;
using SignalMiner.Domain;

namespace SignalMiner.Infrastructure;

// Session lock spans reservation, SMTP submission and durable history; shared by all API instances.
public sealed class OutreachLock(SignalMinerDbContext db) : IAsyncDisposable
{
    private bool held;
    public async Task AcquireAsync(CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(731946201)", cancellationToken);
            held = true;
        }
        catch { await db.Database.CloseConnectionAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        try { if (held) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(731946201)"); }
        finally { held = false; await db.Database.CloseConnectionAsync(); }
    }
}

public sealed class OutreachSafetyService(SignalMinerDbContext db, OutreachPolicy policy) : IOutreachSafety
{
    public async Task<OutreachCapacity> CapacityAsync(CancellationToken cancellationToken)
    {
        policy.Validate();
        var start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var used = await db.EmailSubmissions.CountAsync(x => (x.SubmittedAt ?? x.StartedAt) >= start, cancellationToken);
        return new(policy.DailySendLimit, policy.DelayBetweenMessagesSeconds, Math.Max(0, policy.DailySendLimit - used));
    }

    public async Task<IOutreachSendLease> BeginAsync(Guid leadId, IReadOnlyList<string> recipients, CancellationToken cancellationToken)
    {
        policy.Validate();
        var emails = recipients.Select(EmailAddress.Normalize).Distinct().ToArray();
        var gate = new OutreachLock(db);
        await gate.AcquireAsync(cancellationToken);
        try
        {
            // Rate pacing is enforced here too, so multiple browsers cannot bypass it.
            var last = await db.EmailSubmissions.OrderByDescending(x => x.SubmittedAt ?? x.StartedAt)
                .Select(x => (DateTimeOffset?)(x.SubmittedAt ?? x.StartedAt)).FirstOrDefaultAsync(cancellationToken);
            if (last is not null)
            {
                var wait = last.Value.AddSeconds(policy.DelayBetweenMessagesSeconds) - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);
            }
            if (await db.EmailSuppressions.AnyAsync(x => emails.Contains(x.NormalizedEmail), cancellationToken))
                throw new ManualEmailException("A recipient is suppressed. Delivery is blocked.", 409);
            if (await db.Leads.AnyAsync(x => x.ContactStatus == ContactStatus.DoNotContact &&
                (x.Id == leadId || (x.PublicEmail != null && emails.Contains(x.PublicEmail.Trim().ToLower()))), cancellationToken))
                throw new ManualEmailException("A recipient is marked DoNotContact. Delivery is blocked.", 409);
            var capacity = await CapacityAsync(cancellationToken);
            if (capacity.RemainingCapacity == 0)
                throw new ManualEmailException("Daily outreach limit reached. Capacity resets at 00:00 UTC.", 429, 0);
            var record = new EmailSubmission { LeadId = leadId, Recipients = string.Join(", ", emails) };
            db.EmailSubmissions.Add(record);
            // Persist before touching SMTP. Interrupted/ambiguous sends retain their reservation.
            await db.SaveChangesAsync(cancellationToken);
            return new Lease(db, gate, record);
        }
        catch { await gate.DisposeAsync(); throw; }
    }

    private sealed class Lease(SignalMinerDbContext db, OutreachLock gate, EmailSubmission record) : IOutreachSendLease
    {
        public async Task SubmittedAsync(EmailDeliveryResult result)
        {
            record.Status = EmailSubmissionStatus.Submitted;
            record.SubmittedAt = result.SubmittedAt;
            record.Details = result.ProviderMessageId;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        public async Task UncertainAsync(string details)
        {
            record.Status = EmailSubmissionStatus.Uncertain;
            record.Details = details;
            db.OutreachEvents.Add(new OutreachEvent { LeadId = record.LeadId, Type = OutreachEventType.Note,
                Body = "Email submission failed or is uncertain; no automatic retry. " + details });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        public ValueTask DisposeAsync() => gate.DisposeAsync();
    }
}

public sealed class SuppressionService(SignalMinerDbContext db) : ISuppressionService
{
    public async Task<EmailSuppression?> RecordAsync(Guid leadId, SuppressionRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Reason)) throw new ManualEmailException("Choose a valid suppression reason.");
        if (request.Details?.Length > 2000) throw new ManualEmailException("Suppression details must be at most 2000 characters.");
        await using var gate = new OutreachLock(db);
        await gate.AcquireAsync(cancellationToken);
        var lead = await db.Leads.FirstOrDefaultAsync(x => x.Id == leadId, cancellationToken);
        if (lead is null) return null;
        var email = EmailAddress.Normalize(lead.PublicEmail ?? "");
        var record = await db.EmailSuppressions.FindAsync([email], cancellationToken);
        var isNew = record is null;
        if (record is null)
        {
            record = new EmailSuppression { NormalizedEmail = email, Reason = request.Reason, Details = request.Details?.Trim(), RelatedLeadId = leadId };
            db.EmailSuppressions.Add(record);
        }
        var matches = await db.Leads.Where(x => x.PublicEmail != null && x.PublicEmail.Trim().ToLower() == email).ToListAsync(cancellationToken);
        foreach (var match in matches)
        {
            if (isNew || match.ContactStatus != ContactStatus.DoNotContact)
                db.OutreachEvents.Add(new OutreachEvent { LeadId = match.Id, Type = OutreachEventType.StatusChanged,
                    NewContactStatus = ContactStatus.DoNotContact, Body = $"Email suppressed: {record.Reason}. {record.Details}" });
            match.ContactStatus = ContactStatus.DoNotContact;
            match.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }
    public async Task<SuppressionSearchResult> SearchAsync(string? query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var records = db.EmailSuppressions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().ToLowerInvariant();
            var reasonMatch = Enum.TryParse<SuppressionReason>(query.Trim(), true, out var reason);
            records = records.Where(x => x.NormalizedEmail.Contains(term) || (x.Details != null && x.Details.ToLower().Contains(term)) || (reasonMatch && x.Reason == reason));
        }
        var total = await records.CountAsync(cancellationToken);
        return new(await records.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.NormalizedEmail)
            .Skip((Math.Clamp(page, 1, 1000000) - 1) * Math.Clamp(pageSize, 1, 100)).Take(Math.Clamp(pageSize, 1, 100)).ToListAsync(cancellationToken), total);
    }
    public async Task<IReadOnlyDictionary<string, EmailSuppression>> FindAsync(IEnumerable<string> emails, CancellationToken cancellationToken)
    {
        var normalized = emails.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant()).Distinct().ToArray();
        return await db.EmailSuppressions.AsNoTracking().Where(x => normalized.Contains(x.NormalizedEmail))
            .ToDictionaryAsync(x => x.NormalizedEmail, cancellationToken);
    }
}
