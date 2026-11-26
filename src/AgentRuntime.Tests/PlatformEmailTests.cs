using System.Net;
using AgentRuntime.Infrastructure.Identity;
using AgentRuntime.Tenancy;

namespace AgentRuntime.Tests;

public sealed class PlatformEmailTests
{
    [Fact]
    public void Html_HasTheBrandedLayout_TheButton_AndItsLinkSpelledOut()
    {
        var email = PlatformAdminService.InvitationEmail("Acme", TenantRole.Admin, "https://app.example.test/login?invite=abc", new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
        var html = PlatformEmailTemplate.RenderHtml(email, "https://app.example.test");

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("You&#39;re invited to Acme", html);
        Assert.Contains(">Accept invitation</a>", html);
        // Once on the button, once as the fallback link's address, once as its text.
        Assert.Equal(3, html.Split("https://app.example.test/login?invite=abc").Length - 1);
        Assert.Contains("expires on 1 December 2026 (UTC)", html);
        Assert.Contains("Open the dashboard", html);
    }

    [Fact]
    public void Text_CarriesTheSameContent_InOrder()
    {
        var email = new PlatformEmail
        {
            Subject = "Subject",
            Greeting = "Hi Ama,",
            Paragraphs = ["First."],
            Quote = "line one\nline two",
            Action = new EmailAction("Sign in", "https://app.example.test/login"),
            Closing = ["After."],
            Footnote = "Why you got this."
        };

        Assert.Equal(
            "Hi Ama,\n\nFirst.\n\n> line one\n> line two\n\nSign in:\nhttps://app.example.test/login\n\nAfter.\n\nWhy you got this.\n\n-- \nTestigo\n",
            PlatformEmailTemplate.RenderText(email));
    }

    /// <summary>Names and use cases come from public forms; none of it may reach the HTML as markup.</summary>
    [Fact]
    public void Html_EncodesEverything_AndOnlyLinksWebAddresses()
    {
        var email = new PlatformEmail
        {
            Subject = "<script>alert(1)</script>",
            Greeting = "Hi <b>Mallory</b>,",
            Paragraphs = ["\"><img src=x onerror=alert(1)>"],
            Quote = "<a href=\"https://evil.test\">click</a>",
            Action = new EmailAction("Go", "javascript:alert(1)"),
            Footnote = "</td><h1>x</h1>"
        };
        var html = PlatformEmailTemplate.RenderHtml(email, "javascript:alert(2)");

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<b>Mallory", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<a href=\"https://evil.test\"", html);
        Assert.DoesNotContain("href=\"javascript:", html);
        Assert.DoesNotContain("<h1>x", html);
        Assert.Contains(WebUtility.HtmlEncode("<b>Mallory</b>"), html);
    }

    [Fact]
    public void FromText_SplitsParagraphs_AndTurnsALoneLinkIntoTheButton()
    {
        var email = PlatformEmail.FromText("Hello", "Hi,\n\nOpen this:\n\nhttps://app.example.test/x\n\nThanks.\n");

        Assert.Equal(["Hi,", "Open this:"], email.Paragraphs);
        Assert.Equal("https://app.example.test/x", email.Action?.Url);
        Assert.Equal(["Thanks."], email.Closing);
        Assert.Null(PlatformEmail.FromText("Hello", "No link here.").Action);
    }
}
