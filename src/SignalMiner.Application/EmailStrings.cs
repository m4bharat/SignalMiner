namespace SignalMiner.Application;

public static class EmailStrings
{
    public const string SenderEmail = "hello@zextri.com";
    public const string DefaultFromName = "SignalMiner";
    public const string TestInboxName = "Zextri Test Inbox";
    public const string SubmittedStatus = "Submitted";
    public const string SentEmailKind = "Sent email";
    public const string TestEmailKind = "Test email";
    public const string UnknownTemplateVersion = "unknown";

    public const string RecipientRequired = "Enter a valid recipient email address.";
    public const string DoNotContact = "This lead is marked DoNotContact. Email sending is disabled.";
    public const string LeadEmailRequired = "This lead does not have a saved email address. Add the email to the lead before sending.";
    public const string RecipientMismatch = "Recipient email does not match the selected lead. Refresh the lead and try again.";
    public const string SubjectRequired = "Email subject is required.";
    public const string BodyRequired = "Email body is required.";
    public const string AttachmentsTooLarge = "Attachments are too large. Keep total attachment size under 10 MB.";
    public const string SmtpNotConfigured = "Email sending is not configured. Set Email:Smtp host, from email, username, and password.";

    public const string SentTimeLabel = "Sent time:";
    public const string DeliveryStatusLabel = "Delivery status:";
    public const string SubjectLabel = "Subject:";
    public const string ToLabel = "To:";
    public const string TemplateLabel = "Template:";
    public const string ProviderMessageIdLabel = "Provider message ID:";
    public const string TestEmailLabel = "Test email: true";
    public const string ReplyToLabel = "Reply-to:";
    public const string CcLabel = "CC:";
    public const string AttachmentsLabel = "Attachments:";

    public const string EmailCouldNotBeSentPrefix = "Email could not be sent:";
    public const string TitanConnectionFailed = "Titan SMTP connection failed. Check the SMTP host, port, and SSL setting.";
    public const string TitanThirdPartyAccessHelp = "Enable Titan third-party access and use the mailbox password or app password.";
    public const string TitanRejectedSenderHelp = "Enable Titan third-party access for this mailbox and use the mailbox password or app password, then restart the API.";
    public const string TitanLoginFailedPrefix = "Titan SMTP login failed for";
    public const string TitanRejectedSenderPrefix = "Titan rejected sender";
    public const string DetailsLabel = "Details:";

    public static string AttachmentLimit(int maxAttachmentCount) =>
        $"Attach up to {maxAttachmentCount} files per email.";

    public static string InvalidEmail(string fieldName) =>
        $"{fieldName} must be a valid email address.";

    public static string InvalidEmailInList(string fieldName, string email) =>
        $"{fieldName} contains an invalid email address: {email}";

    public static string SmtpConnectionFailed(string details) =>
        $"{EmailCouldNotBeSentPrefix} {TitanConnectionFailed} {DetailsLabel} {details}";

    public static string SendFailed(string details) =>
        $"{EmailCouldNotBeSentPrefix} {details}";

    public static string TitanLoginFailed(string username, string details) =>
        $"{EmailCouldNotBeSentPrefix} {TitanLoginFailedPrefix} {username}. {TitanThirdPartyAccessHelp} {DetailsLabel} {details}";

    public static string TitanRejectedSender(string username) =>
        $"{EmailCouldNotBeSentPrefix} {TitanRejectedSenderPrefix} {username}. {TitanRejectedSenderHelp}";
}
