using System.Text.Json;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Domain.Serialization;

namespace MacroDeckHost.Infrastructure.Persistence;

internal static class PersistenceJsonOptions
{
	public static readonly JsonSerializerOptions Default = Create();

	private static JsonSerializerOptions Create()
	{
		var options = new JsonSerializerOptions(PluginManifestJson.Options);

		// Ahead of the shared JsonStringEnumConverter: a converter registered on the options list is
		// chosen before one declared on the type, so the scope's own converter would never run and the
		// "ActionButton" spelling in every pre-rename file would fail to read.
		options.Converters.Insert(0, new VariableScopeJsonConverter());

		// Same reason: the shared string-enum converter would otherwise write a script input's type in
		// the enum's own casing, while every client is written against the lowercase spelling.
		options.Converters.Insert(1, new ScriptInputTypeJsonConverter());

		return options;
	}
}
