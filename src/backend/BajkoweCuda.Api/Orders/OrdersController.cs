using BajkoweCuda.Api.Auth;
using BajkoweCuda.Api.Problems;
using BajkoweCuda.Application.Common;
using BajkoweCuda.Infrastructure.Identity;
using BajkoweCuda.Infrastructure.Orders;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BajkoweCuda.Api.Orders;

/// <summary>
/// "My orders" (docs/PLAN.md §3.5, §6, <c>/api/orders</c>): the signed-in customer's orders that are not drafts.
/// Someone else's order and a draft answer the same 404, so ids cannot be probed.
/// </summary>
[ApiController]
[Route("api/orders")]
public sealed class OrdersController(UserManager<ApplicationUser> users, OrderService orders) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        return user is null || user.IsDeleted
            ? AuthErrors.NotAuthenticated.ToResult()
            : Ok(await orders.ListForCustomerAsync(user.Id, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeleted)
        {
            return AuthErrors.NotAuthenticated.ToResult();
        }

        return await orders.GetForCustomerAsync(user.Id, id, user.PreferredLanguage, cancellationToken) is { } order
            ? Ok(order)
            : new AppError(OrderApiErrors.NotFound, ErrorKind.NotFound).ToResult();
    }
}
