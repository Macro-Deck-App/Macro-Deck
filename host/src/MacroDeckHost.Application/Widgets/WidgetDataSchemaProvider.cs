using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Json.Schema;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Widgets;

public sealed class WidgetDataSchemaProvider : IWidgetDataSchemaProvider
{
	private static readonly IReadOnlyDictionary<string, string> _resourceNames = new Dictionary<string, string>
	{
		[WidgetTypeIds.ActionButton] = "widget-data-action-button-v2.schema.json",
		[WidgetTypeIds.MusicPlayer] = "widget-data-music-player-v1.schema.json",
		[WidgetTypeIds.Slider] = "widget-data-slider-v1.schema.json",
		[WidgetTypeIds.Weather] = "widget-data-weather-v1.schema.json",
		[WidgetTypeIds.HistoryGraph] = "widget-data-history-graph-v1.schema.json",
		[WidgetTypeIds.Clock] = "widget-data-clock-v1.schema.json"
	};

	private static readonly Lazy<IReadOnlyDictionary<string, JsonSchema>> _schemas = new(LoadSchemas);
	private static readonly Lazy<IReadOnlyDictionary<string, JsonElement>> _raw = new(LoadRaw);

	// A provider's schema is text on a descriptor that can be re-registered at any moment, so it is parsed
	// on demand and cached against the text itself rather than against the type id - a re-registration
	// carrying a changed schema is picked up without anything having to invalidate this.
	private readonly ConcurrentDictionary<string, JsonSchema?> _provided = new(StringComparer.Ordinal);

	private readonly IWidgetTypeRegistry _widgetTypes;

	public WidgetDataSchemaProvider(IWidgetTypeRegistry widgetTypes)
	{
		_widgetTypes = widgetTypes;
	}

	public bool TryGet(string type, [NotNullWhen(true)] out JsonSchema? schema)
	{
		if (_schemas.Value.TryGetValue(type, out var found))
		{
			schema = found;
			return true;
		}

		schema = ProvidedSchemaFor(type);
		return schema is not null;
	}

	public IReadOnlyDictionary<string, JsonElement> All()
	{
		var all = new Dictionary<string, JsonElement>(_raw.Value, StringComparer.Ordinal);

		foreach (var entry in _widgetTypes.All)
		{
			if (all.ContainsKey(entry.WidgetTypeId) || entry.Descriptor.DataSchema is not { } text)
			{
				continue;
			}

			if (TryParse(text, out var element))
			{
				all[entry.WidgetTypeId] = element;
			}
		}

		return all;
	}

	private JsonSchema? ProvidedSchemaFor(string type)
	{
		if (!_widgetTypes.TryResolve(type, out var entry) || entry.Descriptor.DataSchema is not { } text)
		{
			return null;
		}

		return _provided.GetOrAdd(text,
			static candidate =>
			{
				try
				{
					return JsonSchema.FromText(candidate);
				}
				catch (JsonException)
				{
					// Registration already rejects an unreadable schema, so reaching this means the descriptor
					// arrived some other way. One provider's bad schema must not throw out of a save path.
					return null;
				}
			});
	}

	private static bool TryParse(string text, out JsonElement element)
	{
		try
		{
			using var document = JsonDocument.Parse(text);
			element = document.RootElement.Clone();
			return true;
		}
		catch (JsonException)
		{
			element = default;
			return false;
		}
	}

	private static Dictionary<string, JsonSchema> LoadSchemas()
	{
		var assembly = typeof(WidgetDataSchemaProvider).Assembly;
		var schemas = new Dictionary<string, JsonSchema>();

		foreach (var (type, resourceName) in _resourceNames)
		{
			schemas[type] = JsonSchema.FromText(ReadEmbeddedResource(assembly, resourceName));
		}

		return schemas;
	}

	private static Dictionary<string, JsonElement> LoadRaw()
	{
		var assembly = typeof(WidgetDataSchemaProvider).Assembly;
		var raw = new Dictionary<string, JsonElement>();

		foreach (var (type, resourceName) in _resourceNames)
		{
			using var document = JsonDocument.Parse(ReadEmbeddedResource(assembly, resourceName));
			raw[type] = document.RootElement.Clone();
		}

		return raw;
	}

	private static string ReadEmbeddedResource(Assembly assembly, string logicalName)
	{
		using var stream = assembly.GetManifestResourceStream(logicalName) ??
			throw new InvalidOperationException($"'{assembly.FullName}' does not embed '{logicalName}'.");

		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}
}
