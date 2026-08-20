using SignalMiner.Domain;

namespace SignalMiner.Application;

public sealed class ManualEmailService(
    ILeadRepository repository,
    IEmailDeliveryService delivery) : IManualEmailService
{
    private const int MaxAttachmentCount = 5;
    private const int MaxTotalAttachmentBytes = 10 * 1024 * 1024;

    public async Task<Lead?> SendAsync(Guid leadId, SendManualEmailRequest request, CancellationToken cancellationToken)
    {
        var lead = await repository.GetAsync(leadId, cancellationToken);
        if (lead is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(lead.PublicEmail))
        {
            throw new ManualEmailException("This lead does not have a public email address.");
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            throw new ManualEmailException("Email subject is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            throw new ManualEmailException("Email body is required.");
        }

        if (request.Attachments.Count > MaxAttachmentCount)
        {
            throw new ManualEmailException($"Attach up to {MaxAttachmentCount} files per email.");
        }

        if (request.Attachments.Sum(attachment => attachment.Content.Length) > MaxTotalAttachmentBytes)
        {
            throw new ManualEmailException("Attachments are too large. Keep total attachment size under 10 MB.");
        }

        await delivery.SendAsync(
            new EmailMessage(lead.PublicEmail, lead.DisplayName, request.Subject.Trim(), request.Body.Trim(), request.Attachments),
            cancellationToken);

        lead.ContactStatus = ContactStatus.Contacted;
        lead.UpdatedAt = DateTimeOffset.UtcNow;
        lead.OutreachEvents.Add(new OutreachEvent
        {
            Type = OutreachEventType.ManualEmailSent,
            Body = BuildOutreachEventBody(request),
            NewContactStatus = ContactStatus.Contacted
        });

        await repository.SaveChangesAsync(cancellationToken);
        return lead;
    }

    private static string BuildOutreachEventBody(SendManualEmailRequest request)
    {
        var parts = new List<string>
        {
            $"Subject: {request.Subject.Trim()}",
            request.Body.Trim()
        };

        if (request.Attachments.Count > 0)
        {
            parts.Add($"Attachments: {string.Join(", ", request.Attachments.Select(attachment => attachment.FileName))}");
        }

        return string.Join(Environment.NewLine, parts);
    }
}
