using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Users.Features.SendConfirmationEmail;

public sealed record SendConfirmationEmailCommand(Guid UserId) : ICommand;
