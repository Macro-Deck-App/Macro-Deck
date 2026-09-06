using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeckHost.Application.Ui.Sessions;

/// <summary>
/// Masks the values of <c>Secret</c>/<c>Password</c>-typed action parameters before they are put on a
/// config surface's <see cref="UiConfigSurfaceAttributes.Parameters" /> attribute - the same sniff
/// <c>ConfigFlowManager.RememberSecretFields</c> uses to classify a config flow step's fields, applied
/// here to an action's declared <see cref="ActionParameter" /> list instead.
/// </summary>
/// <remarks>
/// Expanding a configuration surface is a UI interaction, not an explicit intent to reveal a stored
/// secret - a card expand must not be the thing that discloses a password to the plugin process
/// (in-process or remote alike; there is no privileged shortcut for an in-process action). The plugin
/// still receives the real value through the ordinary save path, when the user actually submits it -
/// nothing here touches that path.
/// </remarks>
internal static class ActionParameterSecretMasking
{
	private static readonly JsonElement MaskedSentinel =
		JsonSerializer.SerializeToElement(UiConfigSurfaceAttributes.MaskedSecretValue);

	public static Dictionary<string, JsonElement> Mask(
		IReadOnlyList<ActionParameter> definitions,
		IReadOnlyDictionary<string, JsonElement>? values)
	{
		var masked = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
		if (values is null)
		{
			return masked;
		}

		var byName = Index(definitions);

		foreach (var (name, value) in values)
		{
			// A stored value the action no longer declares is masked rather than passed through: a
			// parameter that was Secret before it was removed still has its value on disk, and letting
			// an unrecognised name through would be the one way a secret still crossed in the clear.
			masked[name] = byName.TryGetValue(name, out var definition) ? MaskValue(definition, value) : MaskedSentinel;
		}

		return masked;
	}

	// Tolerant of a duplicate name rather than throwing: a malformed parameter list must not take down
	// the session open, and either declaration masks the same value.
	private static Dictionary<string, ActionParameter> Index(IReadOnlyList<ActionParameter> definitions)
	{
		var byName = new Dictionary<string, ActionParameter>(StringComparer.Ordinal);

		foreach (var definition in definitions)
		{
			byName[definition.Name] = definition;
		}

		return byName;
	}

	private static JsonElement MaskValue(ActionParameter definition, JsonElement value)
	{
		switch (definition.Type)
		{
			case ActionParameterType.Secret:
			case ActionParameterType.Password:
				return MaskedSentinel;

			case ActionParameterType.Object
				when definition.Children is { Count: > 0 } children && value.ValueKind == JsonValueKind.Object:
				return MaskObject(children, value);

			case ActionParameterType.Array
				when definition.ItemTemplate is { } itemTemplate && value.ValueKind == JsonValueKind.Array:
				return MaskArray(itemTemplate, value);

			default:
				return value;
		}
	}

	private static JsonElement MaskObject(IReadOnlyList<ActionParameter> children, JsonElement value)
	{
		var byName = Index(children);
		var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

		foreach (var property in value.EnumerateObject())
		{
			result[property.Name] = byName.TryGetValue(property.Name, out var child)
				? MaskValue(child, property.Value)
				: MaskedSentinel;
		}

		return JsonSerializer.SerializeToElement(result);
	}

	private static JsonElement MaskArray(ActionParameter itemTemplate, JsonElement value)
	{
		var result = new List<JsonElement>();

		foreach (var item in value.EnumerateArray())
		{
			result.Add(MaskValue(itemTemplate, item));
		}

		return JsonSerializer.SerializeToElement(result);
	}
}
