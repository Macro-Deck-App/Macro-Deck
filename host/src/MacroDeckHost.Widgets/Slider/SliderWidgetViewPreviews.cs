using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Widgets.Slider;

internal static class SliderWidgetViewPreviews
{
	[UiPreview("Horizontal", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Horizontal()
		=> SliderWidgetView.Build(new SliderWidgetData
			{
				Label = "Brightness",
				ShowLabel = true,
				ShowValue = true,
				ValueVariable = "brightness",
			},
			new UiState<SliderWidgetReadout>(new SliderWidgetReadout(true, 0, 100, 1, 65, Unit: "%")),
			icon: null,
			sliderEvents: []);

	[UiPreview("Vertical", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Vertical()
		=> SliderWidgetView.Build(new SliderWidgetData
			{
				Label = "Volume",
				IsVertical = true,
				ShowLabel = true,
				ShowValue = true,
				ValueVariable = "volume",
			},
			new UiState<SliderWidgetReadout>(new SliderWidgetReadout(true, 0, 100, 5, 40)),
			icon: null,
			sliderEvents: []);

	[UiPreview("Unbound", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Unbound()
		=> SliderWidgetView.Build(new SliderWidgetData { Label = "Unbound", ShowLabel = true, ShowValue = false },
			new UiState<SliderWidgetReadout>(SliderWidgetReadout.Empty),
			icon: null,
			sliderEvents: []);
}
