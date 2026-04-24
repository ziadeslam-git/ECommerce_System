using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace ECommerce_System.Utilities;

public class EmailSender : IEmailSender
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(IOptions<EmailSettings> settings, ILogger<EmailSender> logger)
    {
        _settings = settings.Value;
        _logger   = logger;
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        try
        {
            // Log credentials status (never log actual values)
            _logger.LogInformation("Attempting to send email to {Email}", email);
            _logger.LogInformation("SMTP Username configured: {Configured}", !string.IsNullOrEmpty(_settings.Username));
            _logger.LogInformation("SMTP Password configured: {Configured}", !string.IsNullOrEmpty(_settings.Password));

            if (string.IsNullOrEmpty(_settings.Username) || string.IsNullOrEmpty(_settings.Password))
            {
                _logger.LogError("Email credentials are missing. Check User Secrets.");
                return;
            }

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
            message.To.Add(MailboxAddress.Parse(email));
            message.Subject = subject;
            message.Body = new TextPart(TextFormat.Html) { Text = htmlMessage };

            using var client = new SmtpClient();
            _logger.LogWarning("DEBUG — Host: smtp.gmail.com | Port: 587 | User: {User} | PassLength: {Len} | SenderEmail: {Sender}",
                _settings.Username, _settings.Password?.Length ?? 0, _settings.SenderEmail);
            await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_settings.Username, _settings.Password!);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent successfully to {Email}", email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}. Error: {Message}", email, ex.Message);
            // Do NOT rethrow — user should see the confirmation page regardless
        }
    }
}
