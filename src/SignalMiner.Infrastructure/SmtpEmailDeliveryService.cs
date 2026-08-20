using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using SignalMiner.Application;

namespace SignalMiner.Infrastructure;

public sealed class SmtpEmailDeliveryService(IConfiguration configuration) : IEmailDeliveryService
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var options = SmtpOptions.From(configuration);
        options.Validate();

        var authenticatedSender = new MailboxAddress(options.FromName, options.Username);
        var mail = new MimeMessage
        {
            Sender = authenticatedSender,
            Subject = message.Subject
        };
        mail.From.Add(authenticatedSender);
        mail.To.Add(new MailboxAddress(message.ToName, message.ToEmail));

        if (!options.FromEmail.Equals(options.Username, StringComparison.OrdinalIgnoreCase))
        {
            mail.ReplyTo.Add(new MailboxAddress(options.FromName, options.FromEmail));
        }

        foreach (var cc in message.Cc)
        {
            mail.Cc.Add(MailboxAddress.Parse(cc));
        }

        foreach (var bcc in message.Bcc)
        {
            mail.Bcc.Add(MailboxAddress.Parse(bcc));
        }

        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
        {
            mail.ReplyTo.Add(MailboxAddress.Parse(message.ReplyTo));
        }

        var body = new BodyBuilder();
        if (message.IsBodyHtml)
        {
            body.HtmlBody = message.Body;
        }
        else
        {
            body.TextBody = message.Body;
        }

        foreach (var attachment in message.Attachments)
        {
            var contentType = string.IsNullOrWhiteSpace(attachment.ContentType)
                ? ContentType.Parse("application/octet-stream")
                : ContentType.Parse(attachment.ContentType);
            body.Attachments.Add(attachment.FileName, attachment.Content, contentType);
        }

        mail.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(options.Host, options.Port, GetSocketOptions(options), cancellationToken);
            await client.AuthenticateAsync(options.Username, options.Password, cancellationToken);
            await client.SendAsync(mail, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (MailKit.Security.AuthenticationException ex)
        {
            throw new ManualEmailException(BuildAuthenticationErrorMessage(options, ex), 502);
        }
        catch (SmtpCommandException ex)
        {
            throw new ManualEmailException(BuildSmtpErrorMessage(ex.Message, options), 502);
        }
        catch (SmtpProtocolException ex)
        {
            throw new ManualEmailException($"Email could not be sent: Titan SMTP connection failed. Check the SMTP host, port, and SSL setting. Details: {ex.Message}", 502);
        }
        catch (InvalidOperationException ex)
        {
            throw new ManualEmailException($"Email could not be sent: {ex.Message}", 502);
        }
    }

    private static SecureSocketOptions GetSocketOptions(SmtpOptions options)
    {
        if (!options.EnableSsl)
        {
            return SecureSocketOptions.None;
        }

        return options.Port == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;
    }

    private static string BuildAuthenticationErrorMessage(SmtpOptions options, MailKit.Security.AuthenticationException ex)
    {
        return $"Email could not be sent: Titan SMTP login failed for {options.Username}. Enable Titan third-party access and use the mailbox password or app password. Details: {ex.Message}";
    }

    private static string BuildSmtpErrorMessage(string message, SmtpOptions options)
    {
        if (message.Contains("Sender address rejected", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not logged in", StringComparison.OrdinalIgnoreCase))
        {
            return $"Email could not be sent: Titan rejected sender {options.Username}. Enable Titan third-party access for this mailbox and use the mailbox password or app password, then restart the API.";
        }

        return $"Email could not be sent: {message}";
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
                int.TryParse(section["Port"], out var port) ? port : 587,
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

            _ = new MailboxAddress(FromName, FromEmail);
            _ = MailboxAddress.Parse(Username);
        }
    }
}
