using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pam.Players.Data;
using Pam.Players.Players.Exceptions;
using Pam.Players.Players.Identity;
using Pam.Players.Players.Models;
using Pam.Players.Players.ValueObjects;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;
using Pam.Shared.Time;

namespace Pam.Players.Players.Features.Register;

public sealed class RegisterPlayerHandler(
    PlayersDbContext db,
    IIdentityProvider identity,
    IClock clock,
    ILogger<RegisterPlayerHandler> logger
) : ICommandHandler<RegisterPlayer, Guid>
{
    public async Task<Guid> Handle(RegisterPlayer cmd, CancellationToken cancellationToken)
    {
        var emailNormalized = cmd.Email.Trim().ToLowerInvariant();

        var exists = await db
            .Players.AsNoTracking()
            .AnyAsync(p => p.Email.Value == emailNormalized, cancellationToken);

        if (exists)
        {
            throw new AlreadyExistsException(
                PlayerErrors.EmailAlreadyRegistered,
                "An account with this email already exists."
            );
        }

        var playerId = PlayerId.New();

        var keycloakId = await identity.CreateUserAsync(
            new CreateIdentityUser(
                Email: emailNormalized,
                FirstName: cmd.FirstName,
                LastName: cmd.LastName,
                Password: cmd.Password,
                RequireEmailVerify: true,
                Attributes: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["player_id"] = playerId.Value.ToString(),
                }
            ),
            cancellationToken
        );

        try
        {
            var player = Player.Register(
                id: playerId,
                identityProviderId: keycloakId.Value,
                email: Email.Create(emailNormalized),
                name: new PersonalName(cmd.FirstName, cmd.LastName),
                dateOfBirth: new DateOfBirth(cmd.DateOfBirth),
                jurisdiction: new Jurisdiction(cmd.CountryCode, cmd.Region),
                asOfUtc: clock.UtcNow
            );

            db.Players.Add(player);
            await db.SaveChangesAsync(cancellationToken);

            await identity.SendVerifyEmailAsync(keycloakId, cancellationToken);

            return playerId.Value;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Registration failed after Keycloak user create; rolling back Keycloak user {KeycloakId}",
                keycloakId.Value
            );
            try
            {
                await identity.DeleteUserAsync(keycloakId, cancellationToken);
            }
            catch (Exception cleanupEx)
            {
                logger.LogError(
                    cleanupEx,
                    "Failed to rollback Keycloak user {KeycloakId} after registration failure",
                    keycloakId.Value
                );
            }
            throw;
        }
    }
}
