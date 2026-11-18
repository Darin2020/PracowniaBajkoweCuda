using BajkoweCuda.Api.Problems;
using BajkoweCuda.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;

namespace BajkoweCuda.Api.Auth;

/// <summary>
/// The signed-in user's own account (docs/PLAN.md §3.2, §6). Every action works on the current user only;
/// there is no user id in the route. Data export and deletion follow in the next part of Phase 3.
/// </summary>
[ApiController]
[Route("api/account")]
public sealed class AccountController(UserManager<ApplicationUser> users) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = await users.GetUserAsync(User);
        return user is null || user.IsDeleted ? AuthErrors.NotAuthenticated.ToResult() : Ok(ToResponse(user));
    }

    [HttpPatch]
    public async Task<IActionResult> Update(UpdateAccountRequest request)
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeleted)
        {
            return AuthErrors.NotAuthenticated.ToResult();
        }

        if (request.DisplayName is { } displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                ModelState.AddModelError(nameof(request.DisplayName), "The display name cannot be empty.");
                return ValidationProblem(ModelState);
            }

            user.DisplayName = displayName.Trim();
        }

        if (request.PreferredLanguage is { } language)
        {
            user.PreferredLanguage = language;
        }

        var result = await users.UpdateAsync(user);
        return result.Succeeded
            ? Ok(ToResponse(user))
            : throw new InvalidOperationException($"Updating the account failed: {string.Join(", ", result.Errors.Select(error => error.Code))}");
    }

    private static AccountResponse ToResponse(ApplicationUser user) => new(
        user.Id,
        user.Email!,
        user.DisplayName,
        user.EmailConfirmed,
        user.PreferredLanguage,
        user.CreatedAt);
}
