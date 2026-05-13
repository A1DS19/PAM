using System.Globalization;
using FluentAssertions;
using Pam.Shared.Audit;
using Pam.Shared.Contracts.Audit;
using Xunit;

namespace Pam.Shared.UnitTests.Audit;

public sealed class SensitiveJsonRedactorTests
{
    public sealed record CreateUser(string Email, [property: Sensitive] string Password);

    public sealed record TokenIssue(
        Guid UserId,
        [property: Sensitive] string AccessToken,
        DateTimeOffset IssuedAt
    );

    public sealed record Plain(string Name, int Count);

    [Fact]
    public void Sensitive_string_property_is_replaced_with_mask()
    {
        var payload = new CreateUser("admin@pam.test", "supersecret");

        var json = SensitiveJsonRedactor.Serialize(payload);

        json.Should().Contain("\"password\":\"***\"");
        json.Should().NotContain("supersecret");
        json.Should().Contain("\"email\":\"admin@pam.test\"");
    }

    [Fact]
    public void Non_sensitive_properties_round_trip_unchanged()
    {
        var payload = new Plain("brand-eu", 42);

        var json = SensitiveJsonRedactor.Serialize(payload);

        json.Should().Be("{\"name\":\"brand-eu\",\"count\":42}");
    }

    [Fact]
    public void Multiple_sensitive_fields_are_each_redacted()
    {
        var payload = new TokenIssue(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "eyJhbGciOi...",
            DateTimeOffset.Parse("2026-05-11T08:00:00Z", CultureInfo.InvariantCulture)
        );

        var json = SensitiveJsonRedactor.Serialize(payload);

        json.Should().Contain("\"accessToken\":\"***\"");
        json.Should().NotContain("eyJhbGciOi");
        json.Should().Contain("11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public void Null_request_serializes_to_literal_null()
    {
        SensitiveJsonRedactor.Serialize(null).Should().Be("null");
    }
}
