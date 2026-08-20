using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using SignalMiner.Application;

namespace SignalMiner.Infrastructure;

public sealed class SmtpEmailDeliveryService(IConfiguration configuration) : IEmailDeliveryService
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var options = SmtpOptions.From(configuration);
        options.Validate();

        using var mail = new MailMessage
        {
            From = new MailAddress(options.FromEmail, options.FromName),
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = message.IsBodyHtml
        };
        mail.To.Add(new MailAddress(message.ToEmail, message.ToName));

        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
        {
            mail.ReplyToList.Add(new MailAddress(message.ReplyTo));
        }

        foreach (var cc in message.Cc)
        {
            mail.CC.Add(new MailAddress(cc));
        }

        foreach (var bcc in message.Bcc)
        {
            mail.Bcc.Add(new MailAddress(bcc));
        }

        foreach (var attachment in message.Attachments)
        {
            mail.Attachments.Add(new Attachment(
                new MemoryStream(attachment.Content),
                attachment.FileName,
                string.IsNullOrWhiteSpace(attachment.ContentType) ? "application/octet-stream" : attachment.ContentType));
        }

        using var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = options.EnableSsl,
            Credentials = new NetworkCredential(options.Username, options.Password),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        try
        {
            await client.SendMailAsync(mail, cancellationToken);
        }
        catch (SmtpException ex)
        {
            throw new ManualEmailException($"Email could not be sent: {ex.Message}", 502);
        }
        catch (InvalidOperationException ex)
        {
            throw new ManualEmailException($"Email could not be sent: {ex.Message}", 502);
        }
    }

    private sealed record SmtpOptions(
        string Host,
        int Port,
        bool EnableSsl,
        string FromEmail,
        string FromName,
        string Username,
        string Password)
    {
        public static SmtpOptions From(IConfiguration configuration)
        {
            var section = configuration.GetSection("Email:Smtp");
            return new SmtpOptions(
                section["Host"] ?? string.Empty,
                int.TryParse(section["Port"], out var port) ? port : 465,
                bool.TryParse(section["EnableSsl"], out var enableSsl) ? enableSsl : true,
                section["FromEmail"] ?? string.Empty,
                section["FromName"] ?? "SignalMiner",
                section["Username"] ?? string.Empty,
                section["Password"] ?? string.Empty);
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Host) ||
                string.IsNullOrWhiteSpace(FromEmail) ||
                string.IsNullOrWhiteSpace(Username) ||
                string.IsNullOrWhiteSpace(Password))
            {
                throw new ManualEmailException("Email sending is not configured. Set Email:Smtp host, from email, username, and password.", 503);
            }
        }
    }
}
