using System.Text.Json.Nodes;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Widgets;

public static class LaunchApplicationButtonJson
{
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
		var flows = ShortPressFlowJson.Build(integrationId, action, values, localization, culture);

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
}
