using BijliGuard.Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace BijliGuard.Infrastructure.Identity;

public sealed class UserAccounts : IUserAccounts
{
    public const string DefaultRole = "User";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly TimeProvider _time;

    public UserAccounts(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        TimeProvider time)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _time = time;
    }

    public async Task<UserAccount?> FindByEmailAsync(string email, CancellationToken ct)
    {
        var user = await _userManager.FindByEmailAsync(email.Trim());
        return user is null ? null : Map(user);
    }

    public async Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : Map(user);
    }

    public async Task<Guid?> CreateAsync(string email, string password, string? fullName, CancellationToken ct)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email.Trim(),
            Email = email.Trim(),
            FullName = fullName?.Trim(),
            EmailConfirmed = false,
            LockoutEnabled = true,
            CreatedAt = _time.GetUtcNow()
        };

        var created = await _userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            // Duplicate email is the only expected failure here; password rules are validated earlier.
            if (created.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName")) return null;
            throw new InvalidOperationException(
                "Identity rejected the account: " + string.Join("; ", created.Errors.Select(e => e.Code)));
        }

        if (!await _roleManager.RoleExistsAsync(DefaultRole))
            await _roleManager.CreateAsync(new IdentityRole<Guid>(DefaultRole));

        await _userManager.AddToRoleAsync(user, DefaultRole);
        return user.Id;
    }

    public async Task<PasswordCheckResult> CheckPasswordAsync(Guid userId, string password, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return PasswordCheckResult.Invalid;

        var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.Succeeded) return PasswordCheckResult.Valid;
        return result.IsLockedOut ? PasswordCheckResult.LockedOut : PasswordCheckResult.Invalid;
    }

    public async Task SetPasswordAsync(Guid userId, string newPassword, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
                   ?? throw new InvalidOperationException("User disappeared during password reset.");

        // Identity's own token flow: the token is generated and consumed here, never exposed.
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Identity rejected the new password: " + string.Join("; ", result.Errors.Select(e => e.Code)));
    }

    public async Task MarkEmailConfirmedAsync(Guid userId, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
                   ?? throw new InvalidOperationException("User disappeared during verification.");
        user.EmailConfirmed = true;
        var updated = await _userManager.UpdateAsync(user);
        if (!updated.Succeeded)
            throw new InvalidOperationException("Could not mark email as confirmed.");
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user is null ? [] : (await _userManager.GetRolesAsync(user)).ToList();
    }

    private static UserAccount Map(ApplicationUser user) =>
        new(user.Id, user.Email ?? user.UserName ?? string.Empty, user.FullName, user.EmailConfirmed);
}
