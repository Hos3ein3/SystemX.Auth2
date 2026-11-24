using System.Text.Json;
using System.Text.Json.Serialization;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Persistence;

/// <summary>Reads and writes <see cref="ProjectSettings"/> as JSON (jsonb column).</summary>
public static class SettingsJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        // jsonb does not keep the order of keys, so the "kind" of a provider may not come first.
        AllowOutOfOrderMetadataProperties = true,
    };

    /// <summary>Writes settings to JSON.</summary>
    public static string Serialize(ProjectSettings settings) => JsonSerializer.Serialize(settings, Options);

    /// <summary>Writes any settings object (for example <see cref="ClientApplicationSettings"/>) to JSON.</summary>
    public static string Serialize<T>(T settings) where T : class => JsonSerializer.Serialize(settings, Options);

    /// <summary>Reads any settings object. An empty or null value gives defaults.</summary>
    public static T Deserialize<T>(string? json) where T : class, new() =>
        string.IsNullOrWhiteSpace(json) ? new T() : JsonSerializer.Deserialize<T>(json, Options) ?? new T();

    /// <summary>Deep copy of any settings object.</summary>
    public static T Clone<T>(T settings) where T : class, new() => Deserialize<T>(Serialize(settings));

    /// <summary>Reads settings from JSON. An empty or null value gives defaults.</summary>
    public static ProjectSettings Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new ProjectSettings() : JsonSerializer.Deserialize<ProjectSettings>(json, Options) ?? new ProjectSettings();

    /// <summary>Reads one external provider (the <c>kind</c> field tells which one).</summary>
    public static ExternalProviderSettings DeserializeProvider(string json) => JsonSerializer.Deserialize<ExternalProviderSettings>(json, Options)!;

    /// <summary>Writes one external provider as a JSON element.</summary>
    public static JsonElement ProviderToElement(ExternalProviderSettings provider)
    {
        var node = JsonSerializer.SerializeToNode<ExternalProviderSettings>(provider, Options)!.AsObject();
        DropProtected(node);
        return JsonSerializer.SerializeToElement(node);
    }

    // The *Protected fields hold encrypted secrets. They are never part of an answer, not even as null.
    private static void DropProtected(System.Text.Json.Nodes.JsonObject node)
    {
        foreach (var name in node.Select(p => p.Key).Where(k => k.EndsWith("Protected", StringComparison.OrdinalIgnoreCase)).ToList()) node.Remove(name);
    }

    /// <summary>Writes the settings with every secret of the external providers removed (for admin answers).</summary>
    public static string SerializeRedacted(ProjectSettings settings, Func<ExternalProviderSettings, ExternalProviderSettings> redact)
    {
        var copy = Clone(settings);
        copy.ExternalProviders = [.. copy.ExternalProviders.Select(redact)];
        var root = JsonSerializer.SerializeToNode(copy, Options)!.AsObject();
        foreach (var provider in root["externalProviders"]?.AsArray() ?? []) if (provider is System.Text.Json.Nodes.JsonObject o) DropProtected(o);
        return root.ToJsonString();
    }

    /// <summary>Deep copy of one external provider.</summary>
    public static ExternalProviderSettings CloneProvider(ExternalProviderSettings provider) =>
        JsonSerializer.Deserialize<ExternalProviderSettings>(JsonSerializer.Serialize<ExternalProviderSettings>(provider, Options), Options)!;

    /// <summary>Deep copy.</summary>
    public static ProjectSettings Clone(ProjectSettings settings) => Deserialize(Serialize(settings));
}
