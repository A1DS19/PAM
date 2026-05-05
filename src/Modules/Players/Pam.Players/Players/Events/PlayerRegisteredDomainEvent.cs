using Pam.Players.Players.Models;
using Pam.Shared.Contracts.DDD;

namespace Pam.Players.Players.Events;

public sealed record PlayerRegisteredDomainEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    PlayerId PlayerId,
    string IdentityProviderId,
    string Jurisdiction
) : IDomainEvent;
