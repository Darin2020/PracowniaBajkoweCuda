using System.Globalization;
using BajkoweCuda.Domain;
using BajkoweCuda.Domain.Orders;
using Microsoft.Extensions.Options;

namespace BajkoweCuda.Infrastructure.Orders;

/// <summary>
/// Section <c>PaymentInstructions</c> (docs/PLAN.md §3.5, §14): one text per language with the placeholders
/// <c>{OrderNumber}</c>, <c>{Amount}</c> and <c>{Currency}</c>. The real texts hold the bank details and live in Key Vault
/// (secrets <c>PaymentInstructions--Templates--pl</c>, ...); appsettings.json has neutral placeholders.
/// </summary>
public sealed class PaymentInstructionsOptions
{
    public const string SectionName = "PaymentInstructions";

    public Dictionary<string, string> Templates { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Fills the payment instructions for an order in the customer's language (English if that one is missing).</summary>
public sealed class PaymentInstructions(IOptions<PaymentInstructionsOptions> options)
{
    public string Render(Order order, string language)
    {
        var lang = Languages.Find(language) ?? Languages.Default;
        var templates = options.Value.Templates;
        var template = templates.GetValueOrDefault(lang) ?? templates.GetValueOrDefault(Languages.Default);
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new InvalidOperationException("PaymentInstructions:Templates is not configured.");
        }

        var culture = CultureInfo.GetCultureInfo(lang);
        return template
            .Replace("{OrderNumber}", order.DisplayNumber, StringComparison.Ordinal)
            .Replace("{Amount}", order.Price.Amount.ToString("N2", culture), StringComparison.Ordinal)
            .Replace("{Currency}", order.Price.Currency, StringComparison.Ordinal);
    }
}
