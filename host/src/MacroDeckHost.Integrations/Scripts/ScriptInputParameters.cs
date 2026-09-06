using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Sdk.Scripts;

namespace MacroDeckHost.Integrations.Scripts;

/// <summary>
/// The call-site convention for script inputs: each value rides as an ordinary block parameter named
/// <c>input:&lt;name&gt;</c>, and the declarations travel to the editor JSON-encoded in an option's
/// metadata under <c>scriptInputs</c>, next to <c>runsOnWidget</c> for whether the chosen script needs a
/// widget target. All three names are part of what the clients are written against.
/// </summary>
internal static class ScriptInputParameters
{
	public const string Prefix = "input:";

	public const string MetadataKey = "scriptInputs";

	public const string RunsOnWidgetMetadataKey = "runsOnWidget";

	private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public static IReadOnlyDictionary<string, object?> Collect(IReadOnlyDictionary<string, object> parameters)
	{
		var inputs = new Dictionary<string, object?>(StringComparer.Ordinal);
		foreach (var (key, value) in parameters)
		{
			if (key.StartsWith(Prefix, StringComparison.Ordinal) && key.Length > Prefix.Length)
			{
				inputs[key[Prefix.Length..]] = value;
			}
		}

		return inputs;
	}

	public static IReadOnlyDictionary<string, string>? Metadata(IReadOnlyList<ScriptInput> inputs, bool runsOnWidget)
	{
		if (inputs.Count == 0 && !runsOnWidget)
		{
			return null;
		}

		var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
		if (inputs.Count > 0)
		{
			metadata[MetadataKey] = JsonSerializer.Serialize(inputs, _options);
		}

		if (runsOnWidget)
		{
			metadata[RunsOnWidgetMetadataKey] = "true";
		}

		return metadata;
	}
}
