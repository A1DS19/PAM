using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.ChangePassword;

// CurrentPassword is required even for the user changing their own password;
// the operator can't perform a stealth takeover from a captured session.
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand;
