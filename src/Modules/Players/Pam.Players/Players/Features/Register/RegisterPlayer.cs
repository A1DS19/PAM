using Pam.Shared.Contracts.CQRS;

namespace Pam.Players.Players.Features.Register;

public sealed record RegisterPlayer(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string CountryCode,
    string? Region
) : ICommand<Guid>;
