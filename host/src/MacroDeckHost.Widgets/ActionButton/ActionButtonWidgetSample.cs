using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Preview;

namespace MacroDeckHost.Widgets.ActionButton;

/// <summary>The card the widget picker draws for the Action Button: a labelled tile in the reader's accent
/// colour. Written as the configuration a button would be stored with rather than as a resolved
/// appearance, so the sample goes through exactly the same parse and cascade a saved button does.</summary>
internal static class ActionButtonWidgetSample
{
	internal static async ValueTask<JsonElement> BuildDataAsync(IWidgetSampleTextResolver text)
	{
		var label = await text.ResolveAsync(AppStrings.Widgets.SamplePreview.ActionButtonLabel())
			.ConfigureAwait(false);

		// No backgroundColor and no icon: absence already means the reader's accent colour, and an icon
		// would have to name an icon pack that this installation may not carry.
		var data = new JsonObject { ["label"] = JsonValue.Create(label) };

		return JsonSerializer.SerializeToElement(data);
	}
}
