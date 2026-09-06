using System.Text.Json;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Widgets.ActionButton;

internal static class ActionButtonWidgetViewPreviews
{
	[UiPreview("Default", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Default() => Build("""{"label":"Lights","backgroundColor":"#2196f3"}""");

	[UiPreview("Long text", Profile = UiPreviewProfiles.Widget)]
	public static UiElement LongText()
		=> Build("""{"label":"Turn off every light in the living room","backgroundColor":"#37474f"}""");

	[UiPreview("Empty", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Empty() => Build("{}");

	private static UiElement Build(string json)
	{
		using var document = JsonDocument.Parse(json);
		var config = ActionButtonWidgetData.Parse(document.RootElement);

		return ActionButtonWidgetView.Build(new UiState<ActionButtonWidgetData>(config),
			new UiState<string?>(config.InitialStateId),
			new UiState<string?>(config.Resolve(config.InitialStateId).Label),
			new UiState<IReadOnlyDictionary<WidgetIconReference, UiResource>>(
				new Dictionary<WidgetIconReference, UiResource>()),
			new UiState<WidgetIconResolution>(new WidgetIconResolution(IsActive: false, Resource: null)),
			imageResource: null,
			events: []);
	}
}
