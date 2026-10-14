using MacroDeck.Sdk.Batteries;
using MacroDeck.Ui.Config;

namespace MacroDeckHost.Widgets.Battery;

internal sealed class BatteryViewStateResolver
{
	public const string ChargingColor = "#32ade6";

	public const string MutedColor = "#8e8e93";

	private static readonly UiThresholds _defaultThresholds = new([
		new UiThresholdBand("red", WidgetThresholds.Red),
		new UiThresholdBand("orange", WidgetThresholds.Orange, 20),
		new UiThresholdBand("yellow", WidgetThresholds.Yellow, 40),
		new UiThresholdBand("green", WidgetThresholds.Green, 60)
	]);

	private readonly IBatteryApi _batteries;

	public BatteryViewStateResolver(IBatteryApi batteries)
	{
		ArgumentNullException.ThrowIfNull(batteries);

		_batteries = batteries;
	}

	public static UiThresholds DefaultThresholds => _defaultThresholds;

	public BatteryViewState Resolve(BatteryWidgetData config)
	{
		ArgumentNullException.ThrowIfNull(config);

		switch (config.Mode)
		{
			case BatteryWidgetData.ModeOne:
				return config.Device is { } device
					? new BatteryViewState { Faces = [FaceFor(device, config)] }
					: BatteryViewState.PickDevice;

			case BatteryWidgetData.ModeSelection:
				return config.Selection.Count == 0
					? BatteryViewState.PickDevice
					: new BatteryViewState
					{
						Faces =
						[
							.. config.Selection.Take(BatteryWidgetData.MaxDevices)
								.Select(device => FaceFor(device, config))
						]
					};

			default:
				var devices = _batteries.Devices;

				return devices.Count == 0
					? BatteryViewState.NoDevices
					: new BatteryViewState
					{
						Faces = [.. devices.Take(BatteryWidgetData.MaxDevices).Select(device => Face(device, config))]
					};
		}
	}

	private BatteryFace FaceFor(BatteryDeviceReference stored, BatteryWidgetData config)
	{
		return _batteries.Find(stored.Id) is { } device
			? Face(device, config)
			: new BatteryFace
			{
				Id = stored.Id,
				Name = stored.Name ?? string.Empty,
				Kind = stored.Kind,
				Status = BatteryFaceStatus.NotAvailable
			};
	}

	public static BatteryFace Face(BatteryDeviceSnapshot device, BatteryWidgetData config)
	{
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(config);

		var face = new BatteryFace
		{
			Id = device.Id,
			Name = device.Name,
			Kind = BatteryDeviceKinds.Normalize(device.Kind),
			Level = device.LevelPercent is { } level ? Math.Clamp(level, 0, 100) : null,
			ChargeState = device.ChargeState,
			Status = device.IsConnected ? BatteryFaceStatus.Live : BatteryFaceStatus.NotConnected,
			MinutesRemaining = Minutes(device.TimeRemaining),
			MinutesToFull = Minutes(device.TimeToFull)
		};

		return face with { Color = ColorOf(face, config) };
	}

	public static string? ColorOf(BatteryFace face, BatteryWidgetData config)
	{
		ArgumentNullException.ThrowIfNull(face);
		ArgumentNullException.ThrowIfNull(config);

		if (face.IsMuted)
		{
			return MutedColor;
		}

		if (config.IsSingleColor)
		{
			return config.Color;
		}

		if (face.IsCharging && config.ChargingColor)
		{
			return ChargingColor;
		}

		return (config.Thresholds ?? _defaultThresholds).ColorAt(face.Level ?? 0);
	}

	private static int? Minutes(TimeSpan? time)
	{
		return time is { } value && value > TimeSpan.Zero
			? (int)Math.Min(Math.Ceiling(value.TotalMinutes), int.MaxValue)
			: null;
	}
}
