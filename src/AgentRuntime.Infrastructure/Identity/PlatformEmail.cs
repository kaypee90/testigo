using System.Net;
using System.Text;

namespace AgentRuntime.Infrastructure.Identity;

/// <summary>The button in a platform email: one thing to do, and where it leads.</summary>
public sealed record EmailAction(string Label, string Url);

/// <summary>
/// One piece of the platform's transactional mail, described by its content rather than its markup.
/// <see cref="PlatformEmailTemplate"/> renders it both as the branded HTML message and as the plain
/// text sent alongside, so every email looks the same and reads well where HTML is turned off.
/// </summary>
public sealed record PlatformEmail
{
    public required string Subject { get; init; }

    /// <summary>The headline inside the message. Defaults to the subject.</summary>
    public string? Heading { get; init; }

    /// <summary>The line shown after the subject in an inbox list. Defaults to the first paragraph.</summary>
    public string? Preview { get; init; }

    /// <summary>e.g. "Hi Ama,".</summary>
    public string? Greeting { get; init; }

    /// <summary>Plain text, one entry per paragraph. Never markup: everything is encoded on the way out.</summary>
    public IReadOnlyList<string> Paragraphs { get; init; } = [];

    /// <summary>Someone else's words (a request's use case), set apart from the platform's own.</summary>
    public string? Quote { get; init; }

    public EmailAction? Action { get; init; }

    /// <summary>Paragraphs after the button.</summary>
    public IReadOnlyList<string> Closing { get; init; } = [];

    /// <summary>Small print under the message: why it was sent, when a link expires.</summary>
    public string? Footnote { get; init; }

    /// <summary>Where a reply goes (e.g. the person who sent feedback). Without one, replies aren't read.</summary>
    public string? ReplyTo { get; init; }

    /// <summary>
    /// Wraps a plain-text body in the template: blank lines separate paragraphs, and the first
    /// paragraph that is only a link becomes the button.
    /// </summary>
    public static PlatformEmail FromText(string subject, string body)
    {
        var before = new List<string>();
        var after = new List<string>();
        EmailAction? action = null;
        foreach (var block in (body ?? string.Empty).Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (action is null && PlatformEmailTemplate.IsWebLink(block)) action = new EmailAction("Open link", block);
            else (action is null ? before : after).Add(block);
        }

        return new PlatformEmail { Subject = subject, Paragraphs = before, Action = action, Closing = after };
    }
}

/// <summary>
/// The one layout every platform email uses: a brand bar, a card with the headline, the message, at
/// most one button (with its link spelled out underneath, for clients that block buttons), and a
/// footer. Built from tables and inline styles, which is what mail clients render reliably; colours
/// follow the dashboard's brand palette, with a dark variant where the client supports one.
/// </summary>
public static class PlatformEmailTemplate
{
    public const string ProductName = "Testigo";

    private const string Brand = "#ff5a43";
    private const string BrandDark = "#ec3b22";
    private const string Ink = "#18181b";
    private const string Body = "#3f3f46";
    private const string Muted = "#71717a";
    private const string Line = "#e4e4e7";
    private const string Canvas = "#f4f4f5";
    private const string Font = "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    /// <summary>Only http(s) links are ever made clickable.</summary>
    public static bool IsWebLink(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsWhiteSpace)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>The text/plain part: the same content, in the same order.</summary>
    public static string RenderText(PlatformEmail email)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(email.Greeting)) parts.Add(email.Greeting.Trim());
        parts.AddRange(email.Paragraphs);
        if (!string.IsNullOrWhiteSpace(email.Quote)) parts.Add(string.Join('\n', email.Quote.Trim().Split('\n').Select(l => "> " + l.TrimEnd())));
        if (email.Action is { } action) parts.Add($"{action.Label}:\n{action.Url}");
        parts.AddRange(email.Closing);
        if (!string.IsNullOrWhiteSpace(email.Footnote)) parts.Add(email.Footnote.Trim());
        parts.Add($"-- \n{ProductName}");
        return string.Join("\n\n", parts) + "\n";
    }

    /// <summary>The text/html part. <paramref name="homeUrl"/> is the dashboard's address, linked from the header.</summary>
    public static string RenderHtml(PlatformEmail email, string? homeUrl = null)
    {
        var heading = E(string.IsNullOrWhiteSpace(email.Heading) ? email.Subject : email.Heading);
        var preview = E(email.Preview ?? email.Paragraphs.FirstOrDefault() ?? email.Subject);
        var home = IsWebLink(homeUrl) ? homeUrl : null;
        var html = new StringBuilder();

        html.Append($$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="color-scheme" content="light dark">
            <meta name="supported-color-schemes" content="light dark">
            <title>{{E(email.Subject)}}</title>
            <style>
              @media (max-width: 620px) {
                .card { padding: 28px 22px !important; }
                .shell { padding: 20px 12px !important; }
              }
              @media (prefers-color-scheme: dark) {
                .canvas { background: #09090b !important; }
                .card { background: #18181b !important; border-color: #27272a !important; border-top-color: #ff5a43 !important; }
                .ink { color: #fafafa !important; }
                .body { color: #d4d4d8 !important; }
                .muted { color: #a1a1aa !important; }
                .quote { background: #27272a !important; }
                .rule { border-color: #27272a !important; }
              }
            </style>
            </head>
            <body class="canvas" style="margin:0;padding:0;background:{{Canvas}};-webkit-text-size-adjust:100%;">
            <div style="display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;">{{preview}}</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" class="canvas" style="background:{{Canvas}};">
            <tr><td align="center" class="shell" style="padding:32px 16px;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:560px;">
            <tr><td style="padding:0 4px 18px;font-family:{{Font}};">

            """);

        var wordmark = $"""<span style="display:inline-block;width:10px;height:10px;border-radius:3px;background:{Brand};margin-right:8px;"></span><span class="ink" style="font-size:18px;font-weight:700;letter-spacing:-0.01em;color:{Ink};">{ProductName}</span>""";
        html.Append(home is null ? wordmark : $"""<a href="{E(home)}" style="text-decoration:none;">{wordmark}</a>""");

        html.Append($"""

            </td></tr>
            <tr><td class="card" style="background:#ffffff;border:1px solid {Line};border-top:3px solid {Brand};border-radius:12px;padding:36px 36px 32px;font-family:{Font};">
            <h1 class="ink" style="margin:0 0 18px;font-size:22px;line-height:1.3;font-weight:700;letter-spacing:-0.01em;color:{Ink};">{heading}</h1>

            """);

        if (!string.IsNullOrWhiteSpace(email.Greeting)) html.Append(Paragraph(email.Greeting.Trim()));
        foreach (var p in email.Paragraphs) html.Append(Paragraph(p));

        if (!string.IsNullOrWhiteSpace(email.Quote))
        {
            html.Append($"""
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 16px;"><tr>
                <td class="quote body" style="background:#fafafa;border-left:3px solid {Brand};border-radius:0 8px 8px 0;padding:12px 16px;font-family:{Font};font-size:14px;line-height:1.6;color:{Body};">{Lines(email.Quote.Trim())}</td>
                </tr></table>

                """);
        }

        if (email.Action is { } action && IsWebLink(action.Url))
        {
            var url = E(action.Url);
            html.Append($"""
                <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:24px 0 20px;"><tr>
                <td align="center" bgcolor="{Brand}" style="background:{Brand};border-radius:8px;">
                <a href="{url}" style="display:inline-block;padding:12px 24px;font-family:{Font};font-size:15px;font-weight:600;line-height:1;color:#ffffff;text-decoration:none;border-radius:8px;">{E(action.Label)}</a>
                </td></tr></table>
                <p class="muted" style="margin:0 0 16px;font-size:13px;line-height:1.6;color:{Muted};">If the button doesn't work, copy this link into your browser:<br><a href="{url}" style="color:{BrandDark};word-break:break-all;">{url}</a></p>

                """);
        }
        else if (email.Action is { } unlinked)
        {
            // Not a web link, so it's never made clickable; shown as text instead.
            html.Append(Paragraph($"{unlinked.Label}: {unlinked.Url}"));
        }

        foreach (var p in email.Closing) html.Append(Paragraph(p));

        if (!string.IsNullOrWhiteSpace(email.Footnote))
        {
            html.Append($"""
                <hr class="rule" style="border:0;border-top:1px solid {Line};margin:24px 0 16px;">
                <p class="muted" style="margin:0;font-size:13px;line-height:1.6;color:{Muted};">{Lines(email.Footnote.Trim())}</p>

                """);
        }

        var dashboard = home is null
            ? string.Empty
            : $""" · <a href="{E(home)}" class="muted" style="color:{Muted};text-decoration:underline;">Open the dashboard</a>""";
        html.Append($"""
            </td></tr>
            <tr><td class="muted" align="center" style="padding:20px 8px 0;font-family:{Font};font-size:12px;line-height:1.6;color:{Muted};">
            Sent by {ProductName}{dashboard}<br>
            {(string.IsNullOrWhiteSpace(email.ReplyTo) ? "This is an automated message about your account; replies aren't read." : $"Replying to this email reaches {E(email.ReplyTo.Trim())}.")}
            </td></tr>
            </table>
            </td></tr>
            </table>
            </body>
            </html>

            """);
        return html.ToString();
    }

    private static string Paragraph(string text) =>
        $"""<p class="body" style="margin:0 0 16px;font-size:15px;line-height:1.65;color:{Body};">{Lines(text)}</p>""" + "\n";

    /// <summary>Encoded text with its line breaks kept.</summary>
    private static string Lines(string text) => E(text).Replace("\r\n", "\n").Replace("\n", "<br>");

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
