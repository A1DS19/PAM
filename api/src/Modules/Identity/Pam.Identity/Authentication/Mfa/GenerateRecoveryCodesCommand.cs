using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.Mfa;

public sealed record GenerateRecoveryCodesCommand : ICommand<GenerateRecoveryCodesResult>;

// Plain-text codes returned ONCE — they're hashed in the user_tokens table
// after this call. The SPA shows them to the user; if the user closes the
// modal without saving them, regenerating invalidates all old codes.
public sealed record GenerateRecoveryCodesResult(IReadOnlyList<string> Codes);
