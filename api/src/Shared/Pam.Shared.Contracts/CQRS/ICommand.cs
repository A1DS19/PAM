using MediatR;

namespace Pam.Shared.Contracts.CQRS;

public interface ICommand : IRequest;

public interface ICommand<out TResponse> : IRequest<TResponse>;

// Opt-out marker for commands that must NOT write to audit.command_log.
//
// The audit pipeline is one row per ICommand with a full redacted payload
// in nvarchar(max). At vendor-ingest volume (millions of transactions per
// day) that 1:1 row would outgrow audit.command_log into a partitioning /
// archival problem fast. Commands marked with this interface still run
// through every other pipeline behavior (validation, caching, OTel,
// logging, outbox flush) — only AuditBehavior short-circuits.
//
// Use only when the business row itself is the audit trail. For
// IngestTransactionCommand, ingest.vendor_transactions already carries
// actor (created_by_*), full payload (the columns are the payload),
// timing (received_at, occurred_at), and status — duplicating that into
// audit.command_log adds no investigative value.
//
// Failures of unaudited commands are still captured by LoggingBehavior
// (warning + exception, structured to Seq) and OpenTelemetryBehavior
// (span exception event). The audit log is not the failure log.
public interface IUnauditedCommand;
