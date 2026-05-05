namespace Pam.Players.Players.ValueObjects;

public sealed record Email
{
    private Email(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Email Create(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        return new Email(raw.Trim().ToLowerInvariant());
    }

    public override string ToString() => Value;
}
