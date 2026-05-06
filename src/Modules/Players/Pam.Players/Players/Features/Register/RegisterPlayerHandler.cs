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

        // Email is unique per (BrandId, Email) — different brands may share
        // the same physical user as separate accounts.
        var exists = await db
            .Players.AsNoTracking()
            .AnyAsync(
                p => p.BrandId == cmd.BrandId && p.Email.Value == emailNormalized,
                cancellationToken
            );

        if (exists)
        {
            throw new AlreadyExistsException(
                PlayerErrors.EmailAlreadyRegistered,
                "An account with this email already exists."
            );
        }

        var playerId = PlayerId.New();

        var idpId = await identity.CreateUserAsync(
            new CreateIdentityUser(
                BrandId: cmd.BrandId,
                Email: emailNormalized,
                FirstName: cmd.FirstName,
                LastName: cmd.LastName,
                Password: cmd.Password,
                RequireEmailVerify: true
            ),
            cancellationToken
        );

        try
        {
            var player = Player.Register(
                id: playerId,
                brandId: cmd.BrandId,
                identityProviderId: idpId.Value,
                email: Email.Create(emailNormalized),
                name: new PersonalName(cmd.FirstName, cmd.LastName),
                dateOfBirth: new DateOfBirth(cmd.DateOfBirth),
                jurisdiction: new Jurisdiction(cmd.CountryCode, cmd.Region),
                asOfUtc: clock.UtcNow
            );

            db.Players.Add(player);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Registration failed before DB save committed; rolling back IDP user {IdpId}",
                idpId.Value
            );
            try
            {
                await identity.DeleteUserAsync(cmd.BrandId, idpId, cancellationToken);
            }
            catch (Exception cleanupEx)
            {
                logger.LogError(
                    cleanupEx,
                    "Failed to rollback IDP user {IdpId} after registration failure",
                    idpId.Value
                );
            }
            throw;
        }

        // Player is durable past this point. Email-send failure must not
        // delete the IDP user — that would orphan the saved row.
        try
        {
            await identity.SendVerifyEmailAsync(cmd.BrandId, idpId, cancellationToken);
        }
        catch (Exception emailEx)
        {
            logger.LogWarning(
                emailEx,
                "Failed to trigger verify-email for {IdpId}; player {PlayerId} is registered but unverified",
                idpId.Value,
                playerId.Value
            );
        }

        return playerId.Value;
    }
}
