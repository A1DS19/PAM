namespace Pam.Notifications.Contracts.Email;

// Cross-module email-sending surface. Any module that needs to send mail
// injects this from `Pam.Notifications.Contracts`; the impl lives in
// `Pam.Notifications`. Lifted out of `Pam.Identity` once it became clear
// every future module (Players welcome emails, Wallet deposit
// confirmations, Bonuses awards, ...) would need email.
//
// **Direct call vs integration event** — see ARCHITECTURE.md:
//
//  - Direct call (this interface): for intra-module flows where the
//    publisher owns the content AND the payload is sensitive enough that
//    we don't want it riding the broker. Identity's password-reset and
//    email-confirm fall here — the reset token IS the credential; putting
//    it in an integration event would fan it out to the outbox, every
//    consumer's logs, and the audit trail.
//
//  - Integration event (preferred for cross-module): the publisher emits
//    a fact ("PlayerRegisteredIntegrationEvent", "DepositCompleted-
//    IntegrationEvent"). A consumer in Pam.Notifications subscribes,
//    queries the publisher's contracts for what it needs (locale,
//    template variables, brand), renders, and calls IEmailSender. Keeps
//    integration events PII-lean and lets Notifications own all template
//    + locale + branding logic.
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
