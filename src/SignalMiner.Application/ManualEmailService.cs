using SignalMiner.Domain;
using System.Net.Mail;

namespace SignalMiner.Application;

public sealed class ManualEmailService(
    ILeadRepository repository,
    IEmailDeliveryService delivery) : IManualEmailService
{
    private const int MaxAttachmentCount = 5;
    private const int MaxTotalAttachmentBytes = 10 * 1024 * 1024;
    private const string TestRecipient = EmailStrings.SenderEmail;

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
            throw new ManualEmailException(EmailStrings.RecipientRequired);
        }

        if (!request.IsTest)
        {
            if (string.IsNullOrWhiteSpace(leadEmail))
            {
                throw new ManualEmailException(EmailStrings.LeadEmailRequired);
            }

            if (!string.Equals(toEmail, leadEmail, StringComparison.OrdinalIgnoreCase))
            {
                throw new ManualEmailException(EmailStrings.RecipientMismatch);
            }
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            throw new ManualEmailException(EmailStrings.SubjectRequired);
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            throw new ManualEmailException(EmailStrings.BodyRequired);
        }

        var cc = ParseEmailList(request.Cc, "CC");
        var bcc = ParseEmailList(request.Bcc, "BCC");
        var replyTo = ParseOptionalEmail(request.ReplyTo, "Reply-to");
        var bodyHtml = string.IsNullOrWhiteSpace(request.BodyHtml) ? null : request.BodyHtml.Trim();

        if (request.Attachments.Count > MaxAttachmentCount)
        {
            throw new ManualEmailException(EmailStrings.AttachmentLimit(MaxAttachmentCount));
        }

        if (request.Attachments.Sum(attachment => attachment.Content.Length) > MaxTotalAttachmentBytes)
        {
            throw new ManualEmailException(EmailStrings.AttachmentsTooLarge);
        }

        var deliveryResult = await delivery.SendAsync(
            new EmailMessage(
                toEmail,
                request.IsTest ? EmailStrings.TestInboxName : lead.DisplayName,
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
            $"{EmailStrings.SentTimeLabel} {deliveryResult.SubmittedAt:u}",
            $"{EmailStrings.DeliveryStatusLabel} {deliveryResult.Status}",
            $"{EmailStrings.SubjectLabel} {request.Subject.Trim()}",
            $"{EmailStrings.ToLabel} {toEmail}",
            request.Body.Trim()
        };

        if (!string.IsNullOrWhiteSpace(request.TemplateId))
        {
            parts.Insert(2, $"{EmailStrings.TemplateLabel} {BuildTemplateSummary(request)}");
        }

        if (!string.IsNullOrWhiteSpace(deliveryResult.ProviderMessageId))
        {
            parts.Insert(2, $"{EmailStrings.ProviderMessageIdLabel} {deliveryResult.ProviderMessageId}");
        }

        if (request.IsTest)
        {
            parts.Insert(0, EmailStrings.TestEmailLabel);
        }

        if (!string.IsNullOrWhiteSpace(request.ReplyTo))
        {
            parts.Add($"{EmailStrings.ReplyToLabel} {request.ReplyTo.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(request.Cc))
        {
            parts.Add($"{EmailStrings.CcLabel} {request.Cc.Trim()}");
        }

        if (request.Attachments.Count > 0)
        {
            parts.Add($"{EmailStrings.AttachmentsLabel} {string.Join(", ", request.Attachments.Select(attachment => attachment.FileName))}");
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string BuildTemplateSummary(SendManualEmailRequest request)
    {
        var templateId = request.TemplateId?.Trim() ?? string.Empty;
        var templateName = request.TemplateName?.Trim();
        var templateCategory = request.TemplateCategory?.Trim();
        var templateVersion = request.TemplateVersion?.Trim() ?? EmailStrings.UnknownTemplateVersion;
        var displayName = string.IsNullOrWhiteSpace(templateName) ? templateId : templateName;
        var category = string.IsNullOrWhiteSpace(templateCategory) ? string.Empty : $" ({templateCategory})";
        var idSuffix = string.IsNullOrWhiteSpace(templateName) ? string.Empty : $" [{templateId}]";

        return $"{displayName}{category} v{templateVersion}{idSuffix}";
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
            throw new ManualEmailException(EmailStrings.InvalidEmail(fieldName));
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
                throw new ManualEmailException(EmailStrings.InvalidEmailInList(fieldName, email));
            }
        }

        return emails;
    }
}
