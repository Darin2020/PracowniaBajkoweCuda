namespace BajkoweCuda.Application.Emails;

/// <summary>Transactional emails (docs/PLAN.md §3.6). Each has localized texts in every supported language.</summary>
public enum EmailTemplate
{
    /// <summary>Link that confirms the email address. Payload: <c>link</c>.</summary>
    ConfirmEmail,

    /// <summary>
    /// Sent instead of a confirmation when someone registers with an address that already has an account, so the
    /// registration response never reveals which addresses are registered. Payload: <c>link</c> (password reset page).
    /// </summary>
    AlreadyRegistered,

    /// <summary>Link to choose a new password. Payload: <c>link</c>.</summary>
    ResetPassword,

    /// <summary>Notice that the password was changed or reset. No payload.</summary>
    PasswordChanged,

    /// <summary>Confirms that the account and its personal data were deleted. No payload.</summary>
    AccountDeleted,

    /// <summary>
    /// An order was confirmed: its number, title and price, and the payment instructions. Always sent, unlike the chat
    /// notifications (docs/DECISIONS.md). Payload: <c>link</c>, <c>orderNumber</c>, <c>title</c>, <c>amount</c>, <c>details</c>.
    /// </summary>
    OrderConfirmed,
}
