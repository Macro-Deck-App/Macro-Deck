using System.Globalization;
using MacroDeckHost.Application.Icons.Included;
using MacroDeckHost.Domain.Widgets;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Preview;

namespace MacroDeckHost.Widgets.Gauges;

internal static class GaugesWidgetSample
{
	internal static async ValueTask<(GaugesWidgetData Config, IReadOnlyList<UiState<GaugeFace>> Faces)> BuildAsync(
		GaugesWidgetData config,
		IWidgetSampleTextResolver text)
	{
		var battery = await text.ResolveAsync(AppStrings.Widgets.SamplePreview.GaugesBattery())
			.ConfigureAwait(false);
		var cpu = await text.ResolveAsync(AppStrings.Widgets.History.PresetCpu()).ConfigureAwait(false);
		var memory = await text.ResolveAsync(AppStrings.Widgets.SamplePreview.GaugesMemory()).ConfigureAwait(false);
		var gpu = await text.ResolveAsync(AppStrings.Widgets.History.PresetGpu()).ConfigureAwait(false);

		(string Id, string Name, string Icon, double Value, string? Color)[] samples =
		[
			("cpu", cpu, IncludedIconPack.Cpu, 42, null),
			("ram", memory, IncludedIconPack.MemoryStick, 71, null),
			("gpu", gpu, IncludedIconPack.Gpu, 18, null),
			("battery", battery, IncludedIconPack.Battery, 86, "#34c759"),
		];

		var sampleConfig = config with
		{
			Title = null,
			Gauges = samples
				.Select(sample => new GaugeConfig
				{
					Id = sample.Id,
					Name = sample.Name,
					Icon = WidgetIconReference.IconPack(IncludedIconPack.IconId(sample.Icon).ToString()),
					Max = 100,
					Color = sample.Color,
				})
				.ToList(),
		};

		IReadOnlyList<UiState<GaugeFace>> faces = samples
			.Select(sample => new UiState<GaugeFace>(new GaugeFace
			{
				Level = sample.Value / 100,
				Value = sample.Value.ToString("0", CultureInfo.InvariantCulture),
				Name = sample.Name,
				Unit = "%",
				Color = sample.Color,
			}))
			.ToList();

		return (sampleConfig, faces);
	}
}
