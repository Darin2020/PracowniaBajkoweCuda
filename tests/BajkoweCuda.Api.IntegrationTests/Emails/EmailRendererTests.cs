using BajkoweCuda.Application.Emails;
using BajkoweCuda.Domain;
using BajkoweCuda.Infrastructure.Emails;

namespace BajkoweCuda.Api.IntegrationTests.Emails;

public sealed class EmailRendererTests
{
    private const string Link = "https://toys.test/en/confirm-email?userId=1&code=abc";

    public static TheoryData<EmailTemplate, string> EveryTemplateInEveryLanguage()
    {
        var data = new TheoryData<EmailTemplate, string>();
        foreach (var template in Enum.GetValues<EmailTemplate>())
        {
            foreach (var language in Languages.All)
            {
                data.Add(template, language);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryTemplateInEveryLanguage))]
    public void Every_template_renders_in_every_language(EmailTemplate template, string language)
    {
        var email = EmailRenderer.Render("anna@example.test", template, language, new Dictionary<string, string> { ["link"] = Link });

        email.To.ShouldBe("anna@example.test");
        email.Subject.ShouldNotBeNullOrWhiteSpace();
        email.HtmlBody.ShouldContain($"lang=\"{language}\"");
        email.TextBody.ShouldContain("Pracownia Bajkowe Cuda");
    }

    [Fact]
    public void Translations_differ_from_english()
    {
        var english = EmailRenderer.Render("a@example.test", EmailTemplate.ConfirmEmail, "en", new Dictionary<string, string> { ["link"] = Link });

        foreach (var language in Languages.All.Where(language => language != "en"))
        {
            EmailRenderer.Render("a@example.test", EmailTemplate.ConfirmEmail, language, new Dictionary<string, string> { ["link"] = Link })
                .Subject.ShouldNotBe(english.Subject, $"{language} should be translated");
        }
    }

    [Fact]
    public void The_link_is_in_both_bodies_and_html_encoded()
    {
        var email = EmailRenderer.Render("a@example.test", EmailTemplate.ResetPassword, "en", new Dictionary<string, string> { ["link"] = Link });

        email.HtmlBody.ShouldContain("href=\"https://toys.test/en/confirm-email?userId=1&amp;code=abc\"");
        email.TextBody.ShouldContain(Link);
    }

    [Fact]
    public void An_unknown_language_falls_back_to_english()
    {
        var email = EmailRenderer.Render("a@example.test", EmailTemplate.PasswordChanged, "xx", new Dictionary<string, string>());

        email.Subject.ShouldBe("Your password was changed");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/link")]
    public void Links_must_be_absolute_http_urls(string link)
    {
        Should.Throw<InvalidOperationException>(() =>
            EmailRenderer.Render("a@example.test", EmailTemplate.ConfirmEmail, "en", new Dictionary<string, string> { ["link"] = link }));
    }

    [Fact]
    public void A_template_with_a_button_needs_a_link()
    {
        Should.Throw<InvalidOperationException>(() =>
            EmailRenderer.Render("a@example.test", EmailTemplate.ConfirmEmail, "en", new Dictionary<string, string>()));
    }

    [Theory]
    [InlineData("anna.nowak@example.com", "a***@example.com")]
    [InlineData("x@y.z", "x***@y.z")]
    [InlineData("not-an-address", "***")]
    public void Addresses_are_masked(string address, string masked)
    {
        EmailAddresses.Mask(address).ShouldBe(masked);
    }

    [Fact]
    public void Addresses_inside_a_text_are_masked()
    {
        EmailAddresses.MaskAll("550 5.1.1 <anna.nowak@example.com>: Recipient address rejected")
            .ShouldBe("550 5.1.1 <a***@example.com>: Recipient address rejected");
    }

    [Theory]
    [MemberData(nameof(Languages_))]
    public void The_chat_notification_renders_in_every_language_for_both_sides(string language)
    {
        foreach (var forCustomer in new[] { true, false })
        {
            var email = EmailRenderer.RenderChatDigest(
                "anna@example.test", language, forCustomer, "Anna", [new DigestItem(false, "Hello", 2), new DigestItem(true, string.Empty, 0)], 3, "https://toys.test/en/chat");

            email.Subject.ShouldNotBeNullOrWhiteSpace();
            email.HtmlBody.ShouldContain($"lang=\"{language}\"");
            email.TextBody.ShouldContain("Hello");
            email.TextBody.ShouldContain("https://toys.test/en/chat");
        }
    }

    public static TheoryData<string> Languages_() => [.. Languages.All];

    [Fact]
    public void The_chat_notification_encodes_message_text_and_counts_the_rest()
    {
        var email = EmailRenderer.RenderChatDigest(
            "owner@example.test", "en", forCustomer: false, "Anna <b>", [new DigestItem(false, "<script>alert(1)</script>", 1)], 4, "https://toys.test/en/admin/conversations/1");

        email.Subject.ShouldBe("New message from Anna <b>");
        email.HtmlBody.ShouldNotContain("<script>");
        email.HtmlBody.ShouldContain("&lt;script&gt;");
        email.HtmlBody.ShouldNotContain("Anna <b>");
        email.TextBody.ShouldContain("Photos: 1");
        email.TextBody.ShouldContain("Further unread messages: 4");
        // Admins have no notification setting to point to.
        email.TextBody.ShouldNotContain("Send replies to my email");
    }

    [Fact]
    public void The_customer_notification_explains_how_to_turn_it_off()
    {
        var email = EmailRenderer.RenderChatDigest("anna@example.test", "en", forCustomer: true, null, [new DigestItem(true, string.Empty, 0)], 0, "https://toys.test/en/chat");

        email.Subject.ShouldBe("New message from Pracownia Bajkowe Cuda");
        email.TextBody.ShouldContain("Update about your order");
        email.TextBody.ShouldContain("Send replies to my email");
    }
}
