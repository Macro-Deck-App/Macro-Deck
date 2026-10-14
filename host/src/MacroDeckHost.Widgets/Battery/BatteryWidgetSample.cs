using MacroDeck.Sdk.Batteries;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Preview;

namespace MacroDeckHost.Widgets.Battery;

internal static class BatteryWidgetSample
{
	internal static async ValueTask<BatteryViewState> BuildAsync(
		BatteryWidgetData config,
		IWidgetSampleTextResolver text)
	{
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(text);

		var phone = await text.ResolveAsync(AppStrings.Widgets.SamplePreview.BatteryPhone()).ConfigureAwait(false);
		var headphones = await text.ResolveAsync(AppStrings.Widgets.SamplePreview.BatteryHeadphones())
			.ConfigureAwait(false);
		var watch = await text.ResolveAsync(AppStrings.Widgets.SamplePreview.BatteryWatch()).ConfigureAwait(false);
		var controller = await text.ResolveAsync(AppStrings.Widgets.SamplePreview.BatteryController())
			.ConfigureAwait(false);

		(string Id, string Name, string Kind, int Level, BatteryChargeState State)[] samples =
		[
			("sample:phone", phone, BatteryDeviceKinds.Phone, 82, BatteryChargeState.Charging),
			("sample:headphones", headphones, BatteryDeviceKinds.Headphones, 54, BatteryChargeState.Discharging),
			("sample:watch", watch, BatteryDeviceKinds.Watch, 31, BatteryChargeState.Discharging),
			("sample:controller", controller, BatteryDeviceKinds.Controller, 12, BatteryChargeState.Discharging)
		];

		return new BatteryViewState
		{
			Faces =
			[
				.. samples.Select(sample => BatteryViewStateResolver.Face(new BatteryDeviceSnapshot
					{
						Id = sample.Id,
						LocalId = sample.Id,
						ProviderId = "sample",
						Name = sample.Name,
						Kind = sample.Kind,
						LevelPercent = sample.Level,
						ChargeState = sample.State,
						IsConnected = true
					},
					config))
			]
		};
	}
}
