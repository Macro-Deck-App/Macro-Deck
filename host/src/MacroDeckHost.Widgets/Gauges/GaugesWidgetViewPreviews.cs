using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Widgets.Gauges;

internal static class GaugesWidgetViewPreviews
{
	[UiPreview("Default", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Default()
		=> Build(new GaugesWidgetData { Title = "System" },
			("CPU", 0.42, "42"),
			("RAM", 0.71, "71"),
			("GPU", 0.93, "93"),
			("Temp", 0.55, "55"));

	[UiPreview("Arc", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Arc()
		=> Build(new GaugesWidgetData { Style = GaugesWidgetData.StyleArc },
			("CPU", 0.42, "42"),
			("Fan", 0.3, "30"));

	[UiPreview("Empty", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Empty() => Build(new GaugesWidgetData());

	private static UiElement Build(GaugesWidgetData config,
		params (string Name, double Level, string Value)[] gauges)
	{
		var configured = config with
		{
			Gauges = gauges
				.Select((gauge, index) => new GaugeConfig
					{ Id = $"g{index}", Name = gauge.Name })
				.ToList(),
		};

		var faces = gauges
			.Select(gauge => new UiState<GaugeFace>(new GaugeFace
				{ Level = gauge.Level, Value = gauge.Value, Unit = "%", Name = gauge.Name }))
			.ToList();

		return GaugesWidgetView.Build(configured, faces);
	}
}
