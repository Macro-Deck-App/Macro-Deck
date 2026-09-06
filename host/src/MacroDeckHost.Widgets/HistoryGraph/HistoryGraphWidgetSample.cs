using System.Globalization;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Preview;

namespace MacroDeckHost.Widgets.HistoryGraph;

/// <summary>The card the widget picker draws for the History Graph: a metric that has been moving. The
/// widget plots a variable's retained history, and no variable is chosen while somebody is picking a
/// widget type, so the window is written out here instead of read.</summary>
internal static class HistoryGraphWidgetSample
{
	// Already normalised onto the chart's plot band, the way the resolver hands a real window over.
	private static readonly double[] _points =
	[
		0.31, 0.34, 0.29, 0.36, 0.44, 0.41, 0.38, 0.47, 0.55, 0.62,
		0.58, 0.51, 0.46, 0.49, 0.57, 0.66, 0.74, 0.71, 0.63, 0.56,
		0.52, 0.48, 0.53, 0.61, 0.69, 0.77, 0.83, 0.79, 0.72, 0.64,
		0.57, 0.5, 0.45, 0.42, 0.47, 0.54, 0.6, 0.55, 0.49, 0.42,
	];

	private const double SampleValue = 42;

	internal static async ValueTask<(HistoryGraphWidgetData Config, HistoryGraphViewState State)> BuildAsync(
		HistoryGraphWidgetData config,
		IWidgetSampleTextResolver text)
	{
		var title = await text.ResolveAsync(AppStrings.Widgets.SamplePreview.HistoryGraphTitle())
			.ConfigureAwait(false);
		var subtitle = await text.ResolveAsync(AppStrings.Widgets.SamplePreview.HistoryGraphSubtitle())
			.ConfigureAwait(false);

		var sampleConfig = config with
		{
			Title = title,
			MaxValue = 100,
			ShowSubtitle = true,
		};

		var state = new HistoryGraphViewState
		{
			Value = SampleValue.ToString("0", CultureInfo.InvariantCulture),
			Unit = "%",
			Digits = 3,
			Subtitle = subtitle,
			Points = _points,
		};

		return (sampleConfig, state);
	}
}
