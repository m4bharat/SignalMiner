using SignalMiner.Domain;
using System.Net.Mail;

namespace SignalMiner.Application;

public sealed class ManualEmailService(
    ILeadRepository repository,
    IEmailDeliveryService delivery) : IManualEmailService
{
    private const int MaxAttachmentCount = 5;
    private const int MaxTotalAttachmentBytes = 10 * 1024 * 1024;
    private const string TestRecipient = "hello@zextri.com";

    public async Task<Lead?> SendAsync(Guid leadId, SendManualEmailRequest request, CancellationToken cancellationToken)
    {
        var lead = await repository.GetAsync(leadId, cancellationToken);
        if (lead is null)
        {
            return null;
        }

        var leadEmail = ParseOptionalEmail(lead.PublicEmail, "Lead email");
        var requestedEmail = ParseOptionalEmail(request.ToEmail, "To");
        var toEmail = request.IsTest ? TestRecipient : requestedEmail;
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            throw new ManualEmailException("Enter a valid recipient email address.");
        }

        if (!request.IsTest)
        {
            if (string.IsNullOrWhiteSpace(leadEmail))
            {
                throw new ManualEmailException("This lead does not have a saved email address. Add the email to the lead before sending.");
            }

            if (!string.Equals(toEmail, leadEmail, StringComparison.OrdinalIgnoreCase))
            {
                throw new ManualEmailException("Recipient email does not match the selected lead. Refresh the lead and try again.");
            }
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            throw new ManualEmailException("Email subject is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            throw new ManualEmailException("Email body is required.");
        }

        var cc = ParseEmailList(request.Cc, "CC");
        var bcc = ParseEmailList(request.Bcc, "BCC");
        var replyTo = ParseOptionalEmail(request.ReplyTo, "Reply-to");
        var bodyHtml = string.IsNullOrWhiteSpace(request.BodyHtml) ? null : request.BodyHtml.Trim();

        if (request.Attachments.Count > MaxAttachmentCount)
        {
            throw new ManualEmailException($"Attach up to {MaxAttachmentCount} files per email.");
        }

        if (request.Attachments.Sum(attachment => attachment.Content.Length) > MaxTotalAttachmentBytes)
        {
            throw new ManualEmailException("Attachments are too large. Keep total attachment size under 10 MB.");
        }

        var deliveryResult = await delivery.SendAsync(
            new EmailMessage(
                toEmail,
                request.IsTest ? "Zextri Test Inbox" : lead.DisplayName,
                request.Subject.Trim(),
                bodyHtml ?? request.Body.Trim(),
                bodyHtml is not null,
                replyTo,
                cc,
                bcc,
                request.Attachments),
            cancellationToken);

        if (!request.IsTest)
        {
            lead.ContactStatus = ContactStatus.Contacted;
        }
        lead.UpdatedAt = DateTimeOffset.UtcNow;
        lead.OutreachEvents.Add(new OutreachEvent
        {
            Type = request.IsTest ? OutreachEventType.ManualEmailPrepared : OutreachEventType.ManualEmailSent,
            Body = BuildOutreachEventBody(request, toEmail, deliveryResult),
            NewContactStatus = request.IsTest ? null : ContactStatus.Contacted
        });

        await repository.SaveChangesAsync(cancellationToken);
        return lead;
    }

    private static string BuildOutreachEventBody(SendManualEmailRequest request, string toEmail, EmailDeliveryResult deliveryResult)
    {
        var parts = new List<string>
        {
            $"Sent time: {deliveryResult.SubmittedAt:u}",
            $"Delivery status: {deliveryResult.Status}",
            $"Subject: {request.Subject.Trim()}",
            $"To: {toEmail}",
            request.Body.Trim()
        };

        if (!string.IsNullOrWhiteSpace(request.TemplateId))
        {
            parts.Insert(2, $"Template: {request.TemplateId.Trim()} v{request.TemplateVersion?.Trim() ?? "unknown"}");
        }

        if (!string.IsNullOrWhiteSpace(deliveryResult.ProviderMessageId))
        {
            parts.Insert(2, $"Provider message ID: {deliveryResult.ProviderMessageId}");
        }

        if (request.IsTest)
        {
            parts.Insert(0, "Test email: true");
        }

        if (!string.IsNullOrWhiteSpace(request.ReplyTo))
        {
            parts.Add($"Reply-to: {request.ReplyTo.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(request.Cc))
        {
            parts.Add($"CC: {request.Cc.Trim()}");
        }

        if (request.Attachments.Count > 0)
        {
            parts.Add($"Attachments: {string.Join(", ", request.Attachments.Select(attachment => attachment.FileName))}");
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string? ParseOptionalEmail(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return new MailAddress(value.Trim()).Address;
        }
        catch (FormatException)
        {
            throw new ManualEmailException($"{fieldName} must be a valid email address.");
        }
    }

    private static IReadOnlyList<string> ParseEmailList(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var emails = value
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var email in emails)
        {
            try
            {
                _ = new MailAddress(email);
            }
            catch (FormatException)
            {
                throw new ManualEmailException($"{fieldName} contains an invalid email address: {email}");
            }
        }

        return emails;
    }
}
