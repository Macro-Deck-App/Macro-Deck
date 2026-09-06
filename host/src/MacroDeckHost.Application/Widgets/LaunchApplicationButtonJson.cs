using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Domain.Widgets;
using TransportParameterType = MacroDeckHost.Application.Ui.Transport.Messages.Actions.ActionParameterType;

namespace MacroDeckHost.Application.Widgets;

public static class LaunchApplicationButtonJson
{
	private const string ShortPressTrigger = "onShortPress";

	private const string ActionBlockColor = "#3b82f6";

	private static readonly JsonSerializerOptions _serializerOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	/// <summary>
	/// <paramref name="localization" /> and <paramref name="culture" /> resolve every piece of action
	/// metadata that lands in the returned JSON: this is stored widget data, so it has to carry finished
	/// text and never a <c>{"$localized":…}</c> reference.
	/// </summary>
	public static string Build(
		string integrationId,
		IActionDefinition action,
		IReadOnlyDictionary<string, string> values,
		string label,
		Guid? iconId,
		ILocalizationResolver localization,
		string? culture)
	{
		var block = new JsonObject
		{
			["id"] = NewBlockId(),
			["type"] = "action",
			["blockType"] = $"{integrationId}.{action.Id}",
			["label"] = localization.Resolve(action.Name, culture) ?? string.Empty,
			["color"] = ActionBlockColor,
			["integrationId"] = integrationId,
			["actionId"] = action.Id,
			["parameters"] = new JsonArray([
				.. action.Parameters.Select(parameter =>
					Parameter(parameter, values, localization, culture))
			])
		};

		var flows = new JsonArray(new JsonObject
		{
			["triggerId"] = ShortPressTrigger,
			["triggerType"] = ShortPressTrigger,
			["children"] = new JsonArray(block)
		});

		var data = new JsonObject
		{
			["label"] = label,
			["labelPosition"] = "bottom",
			["mode"] = "momentary",
			["flows"] = flows.ToJsonString()
		};

		if (iconId is not null)
		{
			data["icon"] = WidgetIconReference.IconPack(iconId.Value.ToString()).ToJson();
		}

		return data.ToJsonString();
	}

	private static JsonObject Parameter(
		ActionParameter parameter,
		IReadOnlyDictionary<string, string> values,
		ILocalizationResolver localization,
		string? culture)
	{
		var def = ActionParameterDefMapper.Map(parameter);
		var node = JsonSerializer.SerializeToNode(def, _serializerOptions)!.AsObject();

		node["type"] = ControlType(def.Type);
		node["label"] = def.Label.IsEmpty ? def.Name : localization.Resolve(def.Label, culture);
		node["description"] = localization.Resolve(def.Description, culture) ?? string.Empty;
		node["placeholder"] = localization.Resolve(def.Placeholder, culture);

		if (node["options"] is JsonArray options && def.Options is { } declared)
		{
			for (var i = 0; i < options.Count && i < declared.Count; i++)
			{
				if (options[i] is JsonObject option)
				{
					option["label"] = localization.Resolve(declared[i].Label, culture);
				}
			}
		}

		node["value"] = values.TryGetValue(parameter.Name, out var value)
			? JsonValue.Create(value)
			: DefaultValue(def);

		return node;
	}

	private static JsonNode? DefaultValue(ActionParameterDef def)
	{
		if (def.DefaultValue is { ValueKind: not JsonValueKind.Null } declared)
		{
			return JsonNode.Parse(declared.GetRawText());
		}

		return def.Type switch
		{
			TransportParameterType.Number or TransportParameterType.Duration => JsonValue.Create(def.Min ?? 0),
			TransportParameterType.Boolean => JsonValue.Create(false),
			TransportParameterType.Choice => JsonValue.Create(def.Options?.FirstOrDefault()?.Value ?? string.Empty),
			TransportParameterType.MultiSelect or TransportParameterType.Array => new JsonArray(),
			TransportParameterType.KeyValue or TransportParameterType.Object => new JsonObject(),
			_ => JsonValue.Create(string.Empty)
		};
	}

	private static string ControlType(TransportParameterType type) => type switch
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

	private static string NewBlockId() => Guid.NewGuid().ToString();
}
