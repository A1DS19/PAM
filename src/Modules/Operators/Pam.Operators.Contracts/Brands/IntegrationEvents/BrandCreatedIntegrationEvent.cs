using Pam.Shared.Messaging.Events;

namespace Pam.Operators.Contracts.Brands.IntegrationEvents;

public sealed record BrandCreatedIntegrationEvent(
    Guid BrandId,
    string Name,
    string Slug,
    string Jurisdiction
) : IntegrationEvent;
