using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Pam.Shared.Contracts.Audit;

namespace Pam.Shared.Audit;

// System.Text.Json contract customization: any property marked
// [Sensitive] gets its converter swapped to one that writes the literal
// JSON string "***" regardless of the property's actual type. The
// original value is still read from the request (the JsonConverter
// receives it), but it never makes it to the buffer — so plaintext
// passwords never reach the audit payload column even in memory beyond
// the converter frame.
public static class SensitiveJsonRedactor
{
    public const string Mask = "***";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { RedactSensitiveProperties },
        },
    };

    public static string Serialize(object? request) =>
        request is null ? "null" : JsonSerializer.Serialize(request, request.GetType(), Options);

    private static void RedactSensitiveProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            if (
                property.AttributeProvider is MemberInfo member
                && member.GetCustomAttribute<SensitiveAttribute>() is not null
            )
            {
                var converterType = typeof(MaskConverter<>).MakeGenericType(property.PropertyType);
                property.CustomConverter = (JsonConverter)Activator.CreateInstance(converterType)!;
            }
        }
    }

    private sealed class MaskConverter<T> : JsonConverter<T>
    {
        public override T? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) => default;

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(Mask);
    }
}
