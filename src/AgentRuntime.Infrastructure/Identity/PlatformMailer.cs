using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentRuntime.Infrastructure.Identity;

/// <summary>The server's own outgoing mail (Email:Smtp): staff invitations and security notices. It never
/// sends anything to reporters.</summary>
public sealed class PlatformEmailOptions
{
    public const string SectionName = "Email";

    public SmtpSettings Smtp { get; set; } = new();

    public sealed class SmtpSettings
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        /// <summary>"starttls" (default) or "none".</summary>
        public string Tls { get; set; } = "starttls";
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        /// <summary>e.g. "Testigo &lt;no-reply@example.com&gt;".</summary>
        public string From { get; set; } = string.Empty;
    }

    public bool Enabled => !string.IsNullOrWhiteSpace(Smtp.Host) && !string.IsNullOrWhiteSpace(Smtp.From);
}

/// <summary>
/// Sends the platform's transactional mail. Without Email:Smtp nothing is sent and callers fall
/// back to showing the operator a link to share; a send that fails is logged and reported as not
/// sent, never thrown, so it can't undo the change that triggered it.
/// </summary>
public sealed class PlatformMailer(IOptions<PlatformEmailOptions> options, IOptions<DashboardOptions> links, ILogger<PlatformMailer> logger)
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(15);

    public bool Enabled => options.Value.Enabled;

    /// <summary>Sends the email as branded HTML with a plain-text alternative.</summary>
    public async Task<bool> SendAsync(string to, PlatformEmail email, CancellationToken ct)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(to)) return false;
        var smtp = options.Value.Smtp;
        var subject = email.Subject;
        try
        {
            // The plain text is the body; the HTML rides along as the preferred alternative.
            using var message = new MailMessage(new MailAddress(smtp.From), new MailAddress(to.Trim()))
            {
                Subject = subject,
                SubjectEncoding = Encoding.UTF8,
                Body = PlatformEmailTemplate.RenderText(email),
                BodyEncoding = Encoding.UTF8,
                IsBodyHtml = false
            };
            // A malformed address throws FormatException, caught below like any other bad address.
            if (!string.IsNullOrWhiteSpace(email.ReplyTo)) message.ReplyToList.Add(new MailAddress(email.ReplyTo.Trim()));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                PlatformEmailTemplate.RenderHtml(email, links.Value.BaseUrl), Encoding.UTF8, MediaTypeNames.Text.Html));
            using var client = new SmtpClient(smtp.Host, smtp.Port)
            {
                EnableSsl = !string.Equals(smtp.Tls, "none", StringComparison.OrdinalIgnoreCase),
                DeliveryMethod = SmtpDeliveryMethod.Network
            };
            if (!string.IsNullOrWhiteSpace(smtp.Username)) client.Credentials = new NetworkCredential(smtp.Username, smtp.Password);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(SendTimeout);
            await client.SendMailAsync(message, timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is SmtpException or FormatException or ArgumentException or OperationCanceledException or InvalidOperationException
                                       or System.Net.Sockets.SocketException or IOException)
        {
            logger.LogWarning(ex, "Couldn't send platform email '{Subject}'", subject);
            return false;
        }
    }
}
