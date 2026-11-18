namespace BajkoweCuda.Domain.Chat;

/// <summary>
/// The two sides of a conversation (docs/PLAN.md §3.4): the customer, and "the studio", which is any admin.
/// Read state is kept per side, not per admin.
/// </summary>
public enum ChatSide
{
    Customer = 0,
    Studio = 1,
}
