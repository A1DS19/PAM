using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pam.Shared.Contracts.Caching;

public static class CacheKeyGenerator
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string GenerateKey<T>(T request, string? pattern = null)
    {
        if (!string.IsNullOrEmpty(pattern) && request is not null)
        {
            return InterpolatePattern(pattern, request);
        }

        var typeName = typeof(T).Name;
        var hash = ComputeHash(JsonSerializer.Serialize(request, SerializerOptions));
        return $"{typeName}:{hash}";
    }

    public static string GenerateKey(Type requestType, object request, string? pattern = null)
    {
        if (!string.IsNullOrEmpty(pattern))
        {
            return InterpolatePattern(pattern, request);
        }

        var hash = ComputeHash(JsonSerializer.Serialize(request, SerializerOptions));
        return $"{requestType.Name}:{hash}";
    }

    private static string InterpolatePattern(string pattern, object request)
    {
        var result = pattern;
        foreach (var prop in request.GetType().GetProperties())
        {
            var placeholder = $"{{{prop.Name}}}";
            var value = prop.GetValue(request)?.ToString() ?? "null";
            result = result.Replace(placeholder, value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static string ComputeHash(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
