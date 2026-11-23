using System.Text.Json;
using System.Text.Json.Serialization;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Persistence;

/// <summary>Reads and writes <see cref="ApplicationSettings"/> as JSON (jsonb column).</summary>
public static class SettingsJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        // jsonb does not keep the order of keys, so the "kind" of a provider may not come first.
        AllowOutOfOrderMetadataProperties = true,
    };

    /// <summary>Writes settings to JSON.</summary>
    public static string Serialize(ApplicationSettings settings) => JsonSerializer.Serialize(settings, Options);

    /// <summary>Reads settings from JSON. An empty or null value gives defaults.</summary>
    public static ApplicationSettings Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new ApplicationSettings() : JsonSerializer.Deserialize<ApplicationSettings>(json, Options) ?? new ApplicationSettings();

    /// <summary>Deep copy of one external provider.</summary>
    public static ExternalProviderSettings CloneProvider(ExternalProviderSettings provider) =>
        JsonSerializer.Deserialize<ExternalProviderSettings>(JsonSerializer.Serialize<ExternalProviderSettings>(provider, Options), Options)!;

    /// <summary>Deep copy.</summary>
    public static ApplicationSettings Clone(ApplicationSettings settings) => Deserialize(Serialize(settings));
}
