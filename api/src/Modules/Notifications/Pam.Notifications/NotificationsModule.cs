using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pam.Notifications.Contracts.Email;
using Pam.Notifications.Email;

namespace Pam.Notifications;

// Module-shaped following the Pam.Operators pattern, but minimal —
// no DbContext yet (no aggregates, no audit log). Surface today is
// just `IEmailSender` + the SMTP impl. When templates / locales /
// send-audit-log become real concerns, this module grows a DbContext
// and the Consumers/ folder fills up.
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services
            .AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName))
            .ValidateOnStart();

        services.AddScoped<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
