namespace Pam.Identity.Contracts.Permissions;

// Custom claim types injected at token issuance. Other modules use these as
// constants in [Authorize(Policy = ...)] / RequireClaim() — the strings are
// part of the cross-module contract.
public static class PamClaimTypes
{
    // Fine-grained per-action authz. Multi-valued: one Permission claim per
    // granted code (`operators.brands.write`, `identity.users.read`, …).
    public const string Permission = "pam_permission";

    // Brand scope of a back-office user. Players, when re-introduced, will
    // also carry a brand_id but issued under a different audience.
    public const string BrandId = "brand_id";
}
