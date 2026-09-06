using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeck.Plugin.Packaging.Manifest;

/// <summary>
/// The single serializer configuration for <c>manifest.json</c> and every other on-disk JSON document
/// Macro Deck reads or writes. <see cref="Options"/> is the one instance: the host's own
/// <c>PersistenceJsonOptions.Default</c> returns this same object rather than constructing its own, so
/// a manifest can never be parsed by two divergent configurations. camelCase properties, indented,
/// string enums. Reading is case-insensitive and tolerates trailing commas, so older PascalCase files
/// and hand-edited ones still load.
/// </summary>
public static class PluginManifestJson
{
	public static readonly JsonSerializerOptions Options = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		AllowTrailingCommas = true,
		Converters = { new JsonStringEnumConverter() }
	};
}
