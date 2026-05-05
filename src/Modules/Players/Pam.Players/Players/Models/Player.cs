using Pam.Players.Players.Events;
using Pam.Players.Players.Exceptions;
using Pam.Players.Players.ValueObjects;
using Pam.Shared.Contracts.Identity;
using Pam.Shared.DDD;
using Pam.Shared.Exceptions;

namespace Pam.Players.Players.Models;

public sealed class Player : Aggregate<PlayerId>
{
    public const int MinimumAge = 18;

    private Player() { }

    public string IdentityProviderId { get; private set; } = default!;

    public Email Email { get; private set; } = default!;

    public PersonalName Name { get; private set; } = default!;

    public DateOfBirth DateOfBirth { get; private set; } = default!;

    public Jurisdiction Jurisdiction { get; private set; } = default!;

    public PlayerStatus Status { get; private set; }

    public bool EmailVerified { get; private set; }

    public static Player Register(
        PlayerId id,
        string identityProviderId,
        Email email,
        PersonalName name,
        DateOfBirth dateOfBirth,
        Jurisdiction jurisdiction,
        DateTimeOffset asOfUtc
    )
    {
        var age = dateOfBirth.AgeAt(asOfUtc);
        if (age < MinimumAge)
        {
            throw new BusinessRuleViolationException(
                PlayerErrors.AgeBelowMinimum,
                $"Player must be at least {MinimumAge} years old."
            );
        }

        var player = new Player
        {
            Id = id,
            IdentityProviderId = identityProviderId,
            Email = email,
            Name = name,
            DateOfBirth = dateOfBirth,
            Jurisdiction = jurisdiction,
            Status = PlayerStatus.Pending,
            EmailVerified = false,
        };

        player.RaiseDomainEvent(
            new PlayerRegisteredDomainEvent(
                EventId: PamIds.New(),
                OccurredAt: asOfUtc,
                PlayerId: id,
                IdentityProviderId: identityProviderId,
                Jurisdiction: jurisdiction.ToString()
            )
        );

        return player;
    }
}
