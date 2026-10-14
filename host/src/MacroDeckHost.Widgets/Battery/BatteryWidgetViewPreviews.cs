using MacroDeck.Sdk.Batteries;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Widgets.Battery;

internal static class BatteryWidgetViewPreviews
{
	[UiPreview("Single", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Single()
	{
		return Build(new BatteryWidgetData(), Face("Phone", BatteryDeviceKinds.Phone, 82, BatteryChargeState.Charging));
	}

	[UiPreview("Several", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Several()
	{
		return Build(new BatteryWidgetData(),
			Face("Phone", BatteryDeviceKinds.Phone, 82, BatteryChargeState.Charging),
			Face("Headphones", BatteryDeviceKinds.Headphones, 54, BatteryChargeState.Discharging),
			Face("Watch", BatteryDeviceKinds.Watch, 31, BatteryChargeState.Discharging),
			Face("Controller", BatteryDeviceKinds.Controller, 12, BatteryChargeState.Discharging));
	}

	[UiPreview("Bars", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Bars()
	{
		return Build(new BatteryWidgetData { Style = BatteryWidgetData.StyleBar },
			Face("Phone", BatteryDeviceKinds.Phone, 82, BatteryChargeState.Charging),
			Face("Mouse", BatteryDeviceKinds.Mouse, 18, BatteryChargeState.Discharging),
			Face("Keyboard", BatteryDeviceKinds.Keyboard, null, BatteryChargeState.Unknown));
	}

	[UiPreview("Empty", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Empty()
	{
		return BatteryWidgetView.Build(new UiState<BatteryViewState>(BatteryViewState.NoDevices),
			new BatteryWidgetData());
	}

	private static UiElement Build(BatteryWidgetData config, params BatteryDeviceSnapshot[] devices)
	{
		return BatteryWidgetView.Build(new UiState<BatteryViewState>(new BatteryViewState
			{
				Faces = [.. devices.Select(device => BatteryViewStateResolver.Face(device, config))]
			}),
			config);
	}

	private static BatteryDeviceSnapshot Face(string name, string kind, int? level, BatteryChargeState state)
	{
		return new BatteryDeviceSnapshot
		{
			Id = $"preview:{name}",
			LocalId = name,
			ProviderId = "preview",
			Name = name,
			Kind = kind,
			LevelPercent = level,
			ChargeState = state,
			IsConnected = true
		};
	}
}
