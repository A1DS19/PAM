using Pam.Shared.Contracts.Identity;
using Pam.Shared.DDD;

namespace Pam.Players.Players.Models;

// Scaffold-only aggregate. Carries just enough to validate the
// brand-scoped-per-module pattern (BrandId on every row) + give the
// initial migration something to create. Real shape (display name,
// locale, KYC status, self-exclusion flags, …) lands as separate
// features per the per-module pattern in ARCHITECTURE.md.
public sealed class Player : Aggregate<Guid>
{
    public Guid BrandId { get; private set; }

    public string Email { get; private set; } = default!;

    private Player() { }

    public static Player Create(Guid brandId, string email)
    {
        return new Player
        {
            Id = PamIds.New(),
            BrandId = brandId,
            Email = email,
        };
    }
}
