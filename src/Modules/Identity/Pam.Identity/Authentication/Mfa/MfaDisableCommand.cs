using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.Mfa;

// Self-disable. CurrentPassword is required so a captured session cookie
// can't silently weaken the user's auth posture.
public sealed record MfaDisableCommand(string CurrentPassword) : ICommand;
