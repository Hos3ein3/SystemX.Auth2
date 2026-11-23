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
    };

    /// <summary>Writes settings to JSON.</summary>
    public static string Serialize(ApplicationSettings settings) => JsonSerializer.Serialize(settings, Options);

    /// <summary>Reads settings from JSON. An empty or null value gives defaults.</summary>
    public static ApplicationSettings Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new ApplicationSettings() : JsonSerializer.Deserialize<ApplicationSettings>(json, Options) ?? new ApplicationSettings();

    /// <summary>Makes a deep copy.</summary>
    public static ApplicationSettings Clone(ApplicationSettings settings) => Deserialize(Serialize(settings));
}
