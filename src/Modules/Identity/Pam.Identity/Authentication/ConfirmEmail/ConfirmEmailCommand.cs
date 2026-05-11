using Pam.Shared.Contracts.Audit;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.ConfirmEmail;

public sealed record ConfirmEmailCommand(string Email, [property: Sensitive] string Token)
    : ICommand;
