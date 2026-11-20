namespace BajkoweCuda.Application.Chat;

/// <summary>Absolute links to the chat pages of the Angular app, for the chat emails (docs/PLAN.md §3.6).</summary>
public interface IChatLinks
{
    /// <summary>The customer's chat, in their language (<c>/{lang}/chat</c>).</summary>
    string CustomerChat(string language);

    /// <summary>One conversation in the admin area (<c>/{lang}/admin/conversations/{id}</c>).</summary>
    string AdminConversation(Guid conversationId, string language);

    /// <summary>One of the customer's orders (<c>/{lang}/orders/{id}</c>).</summary>
    string CustomerOrder(Guid orderId, string language);
}
