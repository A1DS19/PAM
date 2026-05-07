using Pam.Shared.Contracts.DDD;
using Pam.Shared.Contracts.Identity;

namespace Pam.Operators.Brands.Events;

public sealed record BrandCreatedDomainEvent(
    Guid BrandId,
    string Name,
    string Slug,
    string Jurisdiction
) : IDomainEvent
{
    public Guid EventId { get; init; } = PamIds.New();

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
