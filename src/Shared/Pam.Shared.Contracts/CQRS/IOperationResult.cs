namespace Pam.Shared.Contracts.CQRS;

// Opt-in seam for handlers that encode failure in their return shape
// rather than by throwing (e.g. login: "wrong password" is a domain
// outcome, not an exception). AuditBehavior treats Succeeded=false as
// a failed operation and stamps FailureReason into the audit row's
// error_message column. Handlers that throw on failure don't need this.
public interface IOperationResult
{
    bool Succeeded { get; }

    string? FailureReason => null;
}
