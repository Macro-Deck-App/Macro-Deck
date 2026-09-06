using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Preview;

namespace MacroDeckHost.Widgets.Slider;

/// <summary>The card the widget picker draws for the Slider. Only the wording is written here: an unbound
/// slider already draws its own representative level (see <c>SliderWidgetView</c>), and nothing is bound
/// while somebody is choosing a widget type.</summary>
internal static class SliderWidgetSample
{
	internal static async ValueTask<SliderWidgetData> BuildAsync(SliderWidgetData config,
		IWidgetSampleTextResolver text)
	{
		var label = await text.ResolveAsync(AppStrings.Widgets.SamplePreview.SliderLabel()).ConfigureAwait(false);

		return config with { Label = label, ShowLabel = true, ValueVariable = null };
	}
}
