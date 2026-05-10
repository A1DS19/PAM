using Pam.Identity.Authentication.Login;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.LoginMfa;

// Second step of the login flow. Caller has already POSTed /v1/identity/login
// and gotten { mfaRequired: true } — the partial sign-in cookie is set, this
// completes it. RememberMachine bypasses MFA on this device for the
// configured remember-machine TTL.
public sealed record LoginMfaCommand(string Code, bool RememberMachine) : ICommand<LoginResult>;
