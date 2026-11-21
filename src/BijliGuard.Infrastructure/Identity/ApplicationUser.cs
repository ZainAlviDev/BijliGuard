using Microsoft.AspNetCore.Identity;

namespace BijliGuard.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string? FullName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
