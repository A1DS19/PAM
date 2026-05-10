using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.ForgotPassword;

public sealed record ForgotPasswordCommand(string Email) : ICommand;
