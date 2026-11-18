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
}
