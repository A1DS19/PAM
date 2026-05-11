using FluentValidation;

namespace Pam.Ingest.Transactions.Features.Ingest;

public sealed class IngestTransactionValidator : AbstractValidator<IngestTransactionCommand>
{
    public IngestTransactionValidator()
    {
        RuleFor(x => x.VendorId).NotEmpty().MaximumLength(32);

        RuleFor(x => x.VendorReference).NotEmpty().MaximumLength(400);

        RuleFor(x => x.BrandId).NotEmpty();
        RuleFor(x => x.PlayerId).NotEmpty();

        // Zero-amount transactions are nonsense; vendor MUST send a
        // signed value (Risk negative, Win positive). The handler also
        // tolerates this defensively but reject early to keep bad data
        // out of the audit log.
        RuleFor(x => x.AmountCents).NotEqual(0L);

        // ISO 4217 alpha-3. Real currency-table check happens in the
        // handler (rejecting with TransactionStatus.Rejected); here we
        // just enforce format.
        RuleFor(x => x.Currency)
            .NotEmpty()
            .Length(3)
            .Matches("^[A-Z]{3}$")
            .WithMessage("Currency must be ISO 4217 alpha-3 uppercase.");

        RuleFor(x => x.Kind).IsInEnum();

        // Reject transactions claimed to have occurred more than 24h in
        // the future or 30 days in the past. Vendor clock skew happens;
        // 24h is generous. Older-than-30-days replays are almost always
        // misconfiguration.
        RuleFor(x => x.OccurredAt)
            .GreaterThan(DateTimeOffset.UtcNow.AddDays(-30))
            .LessThan(DateTimeOffset.UtcNow.AddHours(24));

        RuleFor(x => x.RoundId).MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(250);
    }
}
