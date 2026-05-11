namespace Pam.Shared.Contracts.Caching;

[AttributeUsage(AttributeTargets.Class)]
public sealed class CacheAttribute : Attribute
{
    public int DurationMinutes { get; }
    public string? KeyPattern { get; }

    public TimeSpan Duration => TimeSpan.FromMinutes(DurationMinutes);

    public CacheAttribute(int durationMinutes = 30, string? keyPattern = null)
    {
        DurationMinutes = durationMinutes;
        KeyPattern = keyPattern;
    }
}
