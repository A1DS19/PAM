using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.Mfa;

public sealed record MfaVerifyCommand(string Code) : ICommand;
