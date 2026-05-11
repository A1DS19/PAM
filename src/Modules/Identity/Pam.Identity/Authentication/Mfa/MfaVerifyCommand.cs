using Pam.Shared.Contracts.Audit;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.Mfa;

public sealed record MfaVerifyCommand([property: Sensitive] string Code) : ICommand;
