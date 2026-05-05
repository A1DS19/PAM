namespace Pam.Players.Players.ValueObjects;

public sealed record Jurisdiction(string CountryCode, string? Region = null)
{
    public override string ToString() => Region is null ? CountryCode : $"{CountryCode}-{Region}";
}
