using FluentAssertions;
using Pam.Players.Players.Events;
using Pam.Players.Players.Exceptions;
using Pam.Players.Players.Models;
using Pam.Players.Players.ValueObjects;
using Pam.Shared.Exceptions;
using Xunit;

namespace Pam.Players.UnitTests.Players;

public class PlayerRegistrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 5, 12, 0, 0, TimeSpan.Zero);
    private const string Brand = "betanything-eu";

    [Fact]
    public void Register_creates_pending_player_with_audit_fields_unset()
    {
        var player = RegisterAt(age: 30);

        player.Status.Should().Be(PlayerStatus.Pending);
        player.EmailVerified.Should().BeFalse();
        // Audit fields are stamped by the EF interceptor, not by the
        // constructor — they remain default until SaveChanges runs.
        player.CreatedAt.Should().Be(default);
    }

    [Fact]
    public void Register_carries_brand_id()
    {
        var player = RegisterAt(age: 30);

        player.BrandId.Should().Be(Brand);
    }

    [Fact]
    public void Register_raises_PlayerRegistered_event_with_brand()
    {
        var player = RegisterAt(age: 30);

        player.DomainEvents.Should().HaveCount(1);
        player.DomainEvents[0].Should().BeOfType<PlayerRegisteredDomainEvent>();

        var ev = (PlayerRegisteredDomainEvent)player.DomainEvents[0];
        ev.PlayerId.Should().Be(player.Id);
        ev.BrandId.Should().Be(Brand);
        ev.IdentityProviderId.Should().Be("idp-sub-123");
        ev.Jurisdiction.Should().Be("US-NY");
        ev.OccurredAt.Should().Be(Now);
    }

    [Fact]
    public void Register_at_exactly_minimum_age_succeeds()
    {
        var act = () => RegisterAt(age: Player.MinimumAge);
        act.Should().NotThrow();
    }

    [Fact]
    public void Register_one_day_below_minimum_age_throws()
    {
        var dob = new DateOfBirth(
            DateOnly.FromDateTime(Now.UtcDateTime).AddYears(-Player.MinimumAge).AddDays(1)
        );

        var act = () =>
            Player.Register(
                id: PlayerId.New(),
                brandId: Brand,
                identityProviderId: "idp-sub-123",
                email: Email.Create("alice@example.com"),
                name: new PersonalName("Alice", "Tester"),
                dateOfBirth: dob,
                jurisdiction: new Jurisdiction("US", "NY"),
                asOfUtc: Now
            );

        var ex = act.Should().Throw<BusinessRuleViolationException>().Which;
        ex.Code.Should().Be(PlayerErrors.AgeBelowMinimum);
    }

    [Fact]
    public void ClearDomainEvents_returns_pending_events_and_empties_the_list()
    {
        var player = RegisterAt(age: 30);

        var drained = player.ClearDomainEvents();

        drained.Should().HaveCount(1);
        player.DomainEvents.Should().BeEmpty();
    }

    private static Player RegisterAt(int age)
    {
        var dob = new DateOfBirth(DateOnly.FromDateTime(Now.UtcDateTime).AddYears(-age));
        return Player.Register(
            id: PlayerId.New(),
            brandId: Brand,
            identityProviderId: "idp-sub-123",
            email: Email.Create("alice@example.com"),
            name: new PersonalName("Alice", "Tester"),
            dateOfBirth: dob,
            jurisdiction: new Jurisdiction("US", "NY"),
            asOfUtc: Now
        );
    }
}
