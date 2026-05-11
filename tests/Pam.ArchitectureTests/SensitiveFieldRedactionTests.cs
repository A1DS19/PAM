using System.Globalization;
using System.Reflection;
using FluentAssertions;
using MediatR;
using Pam.Shared.Contracts.Audit;
using Xunit;

namespace Pam.ArchitectureTests;

// Backstop for the audit redactor: every MediatR command property whose
// name signals a secret (Password, Token, Secret, ApiKey, Credential,
// RefreshToken, AccessToken, PrivateKey, ...) must carry [Sensitive], so
// the audit-log payload column never captures plaintext credentials. A
// regression here means a freshly authored command can ship a real
// password into the `audit.command_log.payload_json` jsonb.
//
// The check is name-pattern based — it can't tell whether a string field
// is genuinely a secret — but the naming convention is enforced
// elsewhere (auth handlers + reviewer instincts), so false positives are
// rare. When one shows up, add the missing [Sensitive] (correct fix) or
// rename the property to something less alarming (the cure is the
// disease).
public sealed class SensitiveFieldRedactionTests
{
    private static readonly string[] SensitiveNameMarkers =
    [
        "password",
        "token",
        "secret",
        "apikey",
        "credential",
        "refreshtoken",
        "accesstoken",
        "privatekey",
        "passphrase",
    ];

    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(Pam.Audit.AuditModule).Assembly,
        typeof(Pam.Identity.IdentityModule).Assembly,
        typeof(Pam.Notifications.NotificationsModule).Assembly,
        typeof(Pam.Operators.OperatorsModule).Assembly,
        typeof(Pam.Players.PlayersModule).Assembly,
        typeof(Pam.Wallet.WalletModule).Assembly,
    ];

    [Fact]
    public void Every_sensitive_named_command_property_carries_the_sensitive_attribute()
    {
        var offenders = ModuleAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(IsMediatRRequest)
            .SelectMany(t =>
                t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Where(LooksLikeASecret)
                    .Where(p => p.GetCustomAttribute<SensitiveAttribute>() is null)
                    .Select(p => $"{t.FullName}.{p.Name}")
            )
            .ToList();

        offenders.Should()
            .BeEmpty(
                "every command property whose name implies a credential must be marked [Sensitive] so the audit redactor masks it. "
                    + "Add [property: Sensitive] on the record positional parameter, or rename the property. Offenders:\n  - "
                    + string.Join("\n  - ", offenders)
            );
    }

    private static bool IsMediatRRequest(Type type)
    {
        if (type.IsAbstract || type.IsInterface)
        {
            return false;
        }
        if (typeof(IBaseRequest).IsAssignableFrom(type))
        {
            return true;
        }
        return type.GetInterfaces()
            .Any(i =>
                i.IsGenericType
                && (
                    i.GetGenericTypeDefinition() == typeof(IRequest<>)
                    || i.GetGenericTypeDefinition() == typeof(INotification)
                )
            );
    }

    private static bool LooksLikeASecret(PropertyInfo property)
    {
        var name = property.Name.ToLower(CultureInfo.InvariantCulture);
        return SensitiveNameMarkers.Any(marker =>
            name.Contains(marker, StringComparison.Ordinal)
        );
    }
}
