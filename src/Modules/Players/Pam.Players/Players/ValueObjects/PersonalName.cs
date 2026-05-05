namespace Pam.Players.Players.ValueObjects;

public sealed record PersonalName(string First, string Last, string? Middle = null)
{
    public string Display => Middle is null ? $"{First} {Last}" : $"{First} {Middle} {Last}";
}
