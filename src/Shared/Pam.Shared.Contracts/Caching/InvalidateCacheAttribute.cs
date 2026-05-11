namespace Pam.Shared.Contracts.Caching;

[AttributeUsage(AttributeTargets.Class)]
public sealed class InvalidateCacheAttribute : Attribute
{
    public string[] Patterns { get; }

    public InvalidateCacheAttribute(params string[] patterns)
    {
        Patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
    }
}
