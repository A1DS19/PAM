using Pam.Shared.Messaging.Events;

namespace Pam.Players.Contracts.Players.Events;

public sealed record PlayerRegisteredIntegrationEvent(
    Guid PlayerId,
    string IdentityProviderId,
    string Jurisdiction
) : IntegrationEvent;
