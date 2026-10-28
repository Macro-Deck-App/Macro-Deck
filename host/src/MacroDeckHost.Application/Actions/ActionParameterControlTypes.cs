using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using TransportParameterType = MacroDeckHost.Application.Ui.Transport.Messages.Actions.ActionParameterType;
using SdkActionParameter = MacroDeck.Sdk.Actions.ActionParameter;
using SdkActionParameterType = MacroDeck.Sdk.Actions.ActionParameterType;

namespace MacroDeckHost.Application.Actions;

// The control type strings are what the desktop UI stores as a block parameter's type
// (mapActionParamType in desktop-ui action-parameter-mapping.util.ts): both sides must stay identical.
public static class ActionParameterControlTypes
{
	public static string Of(SdkActionParameterType type)
	{
		return Of(ActionParameterDefMapper.MapType(type));
	}

	public static string Of(TransportParameterType type)
	{
		return type switch
		{
			TransportParameterType.Number => "number",
			TransportParameterType.Boolean => "boolean",
			TransportParameterType.Choice => "choice",
			TransportParameterType.Password => "password",
			TransportParameterType.Secret => "secret",
			TransportParameterType.DynamicChoice => "dynamic-choice",
			TransportParameterType.Autocomplete => "autocomplete",
			TransportParameterType.MultiSelect => "multiselect",
			TransportParameterType.Color => "color",
			TransportParameterType.File => "file",
			TransportParameterType.Folder => "folder",
			TransportParameterType.Hotkey => "hotkey",
			TransportParameterType.Duration => "duration",
			TransportParameterType.DateTime => "datetime",
			TransportParameterType.Json => "json",
			TransportParameterType.Code => "code",
			TransportParameterType.KeyValue => "keyvalue",
			TransportParameterType.Object => "object",
			TransportParameterType.Array => "array",
			TransportParameterType.IpAddress => "ipaddress",
			TransportParameterType.Url => "url",
			TransportParameterType.Icon => "icon",
			TransportParameterType.Image => "image",
			TransportParameterType.KeyboardSequence => "keyboard-sequence",
			TransportParameterType.KeyboardCombo => "keyboard-combo",
			TransportParameterType.WidgetTarget => "widget-target",
			_ => "string"
		};
	}

	// Mirrors defaultParameterValue in ui/runtime action-flow.util.ts, which fills every declared
	// parameter a stored block lacks when the editor opens it.
	public static JsonNode? EditorDefault(SdkActionParameter parameter)
	{
		var def = ActionParameterDefMapper.Map(parameter);
		if (def.DefaultValue is { ValueKind: not JsonValueKind.Null } declared)
		{
			return JsonNode.Parse(declared.GetRawText());
		}

		return Of(def.Type) switch
		{
			"number" or "duration" => JsonValue.Create(def.Min ?? 0),
			"boolean" => JsonValue.Create(false),
			"choice" => JsonValue.Create(def.Options?.FirstOrDefault()?.Value ?? string.Empty),
			"multiselect" or "array" => new JsonArray(),
			"hotkey" or "password" or "secret" => null,
			"keyvalue" or "object" => new JsonObject(),
			"color" => JsonValue.Create("#3b82f6"),
			"keyboard-sequence" => new JsonObject
				{ ["steps"] = new JsonArray(), ["repeat"] = 1, ["repeatDelayMs"] = 0 },
			"keyboard-combo" => new JsonObject { ["modifiers"] = new JsonArray(), ["key"] = string.Empty },
			_ => JsonValue.Create(string.Empty)
		};
	}
}
