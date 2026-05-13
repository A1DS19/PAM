using Pam.Shared.Messaging.Events;

namespace Pam.Operators.Contracts.Brands.IntegrationEvents;

// IDs + routing only, per ARCHITECTURE.md "Lean integration events".
//   - BrandId : the identity consumers key on
//   - Slug    : routing — many downstream paths (URLs, vendor configs,
//               webhook prefixes) are slug-shaped
//   - Jurisdiction : routing — drives regulatory + locale fan-out
//
// Display name and any other Brand metadata is intentionally NOT on the
// event. Consumers that need it call GetBrandByIdQuery from
// Pam.Operators.Contracts.
public sealed record BrandCreatedIntegrationEvent(
    Guid BrandId,
    string Slug,
    string Jurisdiction
) : IntegrationEvent;
