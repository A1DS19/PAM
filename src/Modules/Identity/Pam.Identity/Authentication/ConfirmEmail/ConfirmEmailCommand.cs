using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.ConfirmEmail;

public sealed record ConfirmEmailCommand(string Email, string Token) : ICommand;
