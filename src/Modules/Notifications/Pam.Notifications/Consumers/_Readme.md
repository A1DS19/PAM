# Consumers

Cross-module integration-event handlers live here. Each consumer:

1. Implements `MassTransit.IConsumer<TIntegrationEvent>` where the event
   is defined in the **publishing** module's `.Contracts` assembly
   (e.g. `Pam.Players.Contracts.../PlayerRegisteredIntegrationEvent`).
2. Receives a lean, fact-shaped event (no PII).
3. Queries the publisher's `Contracts` query interfaces to fetch the
   data needed to render the email (locale, brand, template variables).
4. Renders the template and calls `IEmailSender`.

The consumers are auto-discovered by `AddPamMassTransit(...,
consumerAssemblies)` because `Pam.Notifications` is added to the
`moduleAssemblies` array in `Pam.Api/Program.cs`.

Example skeleton (uncomment when `Pam.Players` lands):

```csharp
// public sealed class PlayerRegisteredEmailConsumer(
//     IPlayerLookup players,
//     IEmailSender emailSender)
//     : IConsumer<PlayerRegisteredIntegrationEvent>
// {
//     public async Task Consume(ConsumeContext<PlayerRegisteredIntegrationEvent> ctx)
//     {
//         var player = await players.GetByIdAsync(ctx.Message.PlayerId, ctx.CancellationToken);
//         // pick template for player.Brand + player.Locale ...
//         await emailSender.SendAsync(new EmailMessage(...), ctx.CancellationToken);
//     }
// }
```

This file is a marker so the empty folder ships in git; delete it once
real consumers land.
