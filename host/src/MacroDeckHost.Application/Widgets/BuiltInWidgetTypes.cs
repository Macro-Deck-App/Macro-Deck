using MacroDeck.Localization;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Widgets;

/// <summary>
/// The descriptors Macro Deck's own widget types register with. They exist so that a built-in type and a
/// provider-registered one are the same kind of thing to every reader downstream: the picker asks the host
/// what a type is called and what a new widget of it starts as, and gets one answer whoever provides it.
/// </summary>
public static class BuiltInWidgetTypes
{
	/// <summary>Whether <paramref name="widgetTypeId" /> names a type Macro Deck itself ships. A provider
	/// can never collide with one: a registered id is always <c>owner::local</c>, and no built-in id
	/// carries the separator.</summary>
	public static bool IsBuiltIn(string? widgetTypeId)
		=> widgetTypeId is not null && WidgetTypeIds.BuiltIn.Contains(widgetTypeId, StringComparer.Ordinal);

	/// <summary>The built-in descriptors, in the order the ids were introduced.</summary>
	/// <param name="configurable">The ids whose in-process provider declares a <c>config</c> surface.</param>
	public static IReadOnlyList<WidgetTypeDescriptor> All(IReadOnlySet<string> configurable)
		=>
		[
			.. WidgetTypeIds.BuiltIn.Select(id => new WidgetTypeDescriptor(id,
				NameOf(id),
				DescriptionOf(id),
				DefaultDataOf(id),
				// A built-in's schema is an embedded resource rather than a descriptor field. See
				// WidgetDataSchemaProvider, which keeps serving those and reads a descriptor's schema only
				// for a type it has no resource for.
				DataSchema: null,
				configurable.Contains(id))),
		];

	private static LocalizedText NameOf(string id) => id switch
	{
		WidgetTypeIds.ActionButton => AppStrings.WebClient.Widgets.ActionButton.Name(),
		WidgetTypeIds.MusicPlayer => AppStrings.WebClient.Widgets.MusicPlayer.Name(),
		WidgetTypeIds.Slider => AppStrings.WebClient.Widgets.Slider.Name(),
		WidgetTypeIds.Weather => AppStrings.WebClient.Widgets.Weather.Name(),
		WidgetTypeIds.HistoryGraph => AppStrings.WebClient.Widgets.HistoryGraph.Name(),
		WidgetTypeIds.Clock => AppStrings.WebClient.Widgets.Clock.Name(),
		_ => default,
	};

	private static LocalizedText DescriptionOf(string id) => id switch
	{
		WidgetTypeIds.ActionButton => AppStrings.WebClient.Widgets.ActionButton.Description(),
		WidgetTypeIds.MusicPlayer => AppStrings.WebClient.Widgets.MusicPlayer.Description(),
		WidgetTypeIds.Slider => AppStrings.WebClient.Widgets.Slider.Description(),
		WidgetTypeIds.Weather => AppStrings.WebClient.Widgets.Weather.Description(),
		WidgetTypeIds.HistoryGraph => AppStrings.WebClient.Widgets.HistoryGraph.Description(),
		WidgetTypeIds.Clock => AppStrings.WebClient.Widgets.Clock.Description(),
		_ => default,
	};

	// What a newly added widget starts with. These were the Angular widget registry's literals until the
	// host became the one place a type's presentation is answered from; the strings among them are stored
	// data the user then edits, not labels the app renders.
	private static string DefaultDataOf(string id) => id switch
	{
		WidgetTypeIds.ActionButton => """{"label":""}""",
		WidgetTypeIds.MusicPlayer => "{}",
		WidgetTypeIds.Slider => """{"orientation":"horizontal","label":"Value"}""",
		WidgetTypeIds.Weather =>
			"""{"showIcon":true,"showTemperature":true,"showCondition":true,"showLocation":true,"showForecast":true,"forecastDays":5}""",
		WidgetTypeIds.HistoryGraph =>
			"""{"valueVariable":"system_cpu_usage_percent","title":"CPU Load","subtitle":"{{ vars.system_cpu_name }}","showSubtitle":true,"unit":"%","maxValue":100}""",
		WidgetTypeIds.Clock => """{"style":"digital","showSeconds":true,"showDate":true}""",
		_ => "{}",
	};
}
