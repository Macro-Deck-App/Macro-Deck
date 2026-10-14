using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Localization;
using MacroDeck.Sdk.Batteries;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.Battery;

internal static class BatteryWidgetConfigView
{
	private static readonly JsonElement _null = JsonSerializer.Deserialize<JsonElement>("null");

	public static UiElement Build(JsonElement data, IBatteryApi batteries)
	{
		ArgumentNullException.ThrowIfNull(batteries);

		var config = BatteryWidgetData.Parse(data);
		var mode = new UiState<string>(config.Mode);

		var device = new UiState<BatteryDeviceReference?>(config.Device is { } stored
			? Refresh(stored, batteries)
			: null);
		var selection = new UiState<IReadOnlyList<BatteryDeviceReference>>([
			.. config.Selection.Select(reference => Refresh(reference, batteries))
		]);

		var style = new UiState<string>(config.Style);
		var showPercentage = new UiState<bool>(config.ShowPercentage);
		var showName = new UiState<bool>(config.ShowName);
		var showChargingBadge = new UiState<bool>(config.ShowChargingBadge);
		var colorMode = new UiState<string>(config.ColorMode);
		var chargingColor = new UiState<bool>(config.ChargingColor);
		var thresholds = new UiState<UiThresholds?>(config.Thresholds);
		var color = new UiState<string>(WidgetConfigJson.ReadString(data, BatteryWidgetData.ColorKey) ?? string.Empty);

		var backgroundColor =
			new UiState<string>(WidgetConfigJson.ReadString(data, "backgroundColor") ?? string.Empty);

		var border = WidgetConfigJson.ReadObject(data, "border");
		var borderStyle = new UiState<string>(WidgetConfigJson.ReadString(border, "style") ?? "off");
		var borderColor = new UiState<string>(WidgetConfigJson.ReadString(border, "color") ?? string.Empty);

		var flows = new UiState<JsonElement>(WidgetConfigJson.ReadFlows(data));

		UiOptionsState? deviceOptions = null;
		deviceOptions = new UiOptionsState(UiOptionSource.From((_, _)
			=> Task.FromResult<IReadOnlyList<UiOption>>(DeviceOptions(batteries,
				device.Peek() is { } picked ? [picked] : []))));
		deviceOptions.Reload();

		var hasDevices = batteries.Devices.Count > 0;

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading { Key = "devices-heading", Text = AppStrings.Widgets.Battery.Devices() },
					new UiChoiceInput
					{
						Key = BatteryWidgetData.ModeKey,
						Label = AppStrings.Widgets.Battery.Devices(),
						HideLabel = true,
						Segmented = true,
						Binding = Bind.To(mode),
						Options = UiValue.Of<IReadOnlyList<UiOption>>([
							UiOption.Of(BatteryWidgetData.ModeOne, AppStrings.Widgets.Battery.DevicesOne()),
							UiOption.Of(BatteryWidgetData.ModeAll, AppStrings.Widgets.Battery.DevicesAll()),
							UiOption.Of(BatteryWidgetData.ModeSelection, AppStrings.Widgets.Battery.DevicesSelection())
						])
					},
					new UiWhen
					{
						Key = "device-when",
						Condition = () => mode.Value == BatteryWidgetData.ModeOne,
						Content = () => DevicePicker(batteries, device, deviceOptions, hasDevices)
					},
					new UiWhen
					{
						Key = "selection-when",
						Condition = () => mode.Value == BatteryWidgetData.ModeSelection,
						Content = () => SelectionPicker(batteries, selection, hasDevices)
					},
					new UiWhen
					{
						Key = "no-devices-when",
						Condition = () => !hasDevices && mode.Value == BatteryWidgetData.ModeAll,
						Content = () => new UiProse
							{ Key = "no-devices", Text = AppStrings.Widgets.Battery.NoDevicesHint() }
					},
					new UiHeading { Key = "style-heading", Text = AppStrings.Widgets.Editor.Style() },
					new UiChoiceInput
					{
						Key = BatteryWidgetData.StyleKey,
						Label = AppStrings.Widgets.Editor.Style(),
						HideLabel = true,
						Segmented = true,
						Binding = Bind.To(style),
						Options = UiValue.Of<IReadOnlyList<UiOption>>([
							UiOption.Of(BatteryWidgetData.StyleRing, AppStrings.Widgets.Gauges.StyleRing()),
							UiOption.Of(BatteryWidgetData.StyleBar, AppStrings.Widgets.Battery.StyleBar())
						])
					},
					new UiHeading { Key = "display-heading", Text = AppStrings.Widgets.Editor.Display() },
					new UiBooleanInput
					{
						Key = BatteryWidgetData.ShowPercentageKey,
						Label = AppStrings.Widgets.Battery.ShowPercentage(),
						Binding = Bind.To(showPercentage)
					},
					new UiBooleanInput
					{
						Key = BatteryWidgetData.ShowNameKey,
						Label = AppStrings.Widgets.Battery.ShowName(),
						Binding = Bind.To(showName)
					},
					new UiBooleanInput
					{
						Key = BatteryWidgetData.ShowChargingBadgeKey,
						Label = AppStrings.Widgets.Battery.ShowChargingBadge(),
						Binding = Bind.To(showChargingBadge)
					},
					new UiHeading { Key = "color-heading", Text = AppStrings.Widgets.Battery.ColorMode() },
					new UiChoiceInput
					{
						Key = BatteryWidgetData.ColorModeKey,
						Label = AppStrings.Widgets.Battery.ColorMode(),
						HideLabel = true,
						Segmented = true,
						Binding = Bind.To(colorMode),
						Options = UiValue.Of<IReadOnlyList<UiOption>>([
							UiOption.Of(BatteryWidgetData.ColorByLevel, AppStrings.Widgets.Battery.ColorByLevel()),
							UiOption.Of(BatteryWidgetData.ColorSingle, AppStrings.Widgets.Battery.ColorSingle())
						])
					},
					new UiWhen
					{
						Key = "level-when",
						Condition = () => colorMode.Value != BatteryWidgetData.ColorSingle,
						Content = () => new UiFragment
						{
							Key = "level-fields",
							Children =
							[
								new UiThresholdsInput
								{
									Key = WidgetThresholds.ValueKey,
									AllowVariables = true,
									Label = AppStrings.Widgets.Editor.ColorThresholds(),
									Binding = Bind.Custom(() => thresholds.Value!, value => thresholds.Value = value),
									Min = 0d,
									Max = 100d,
									Step = 1d,
									Unit = UiText.Of("%"),
									DefaultValue = BatteryViewStateResolver.DefaultThresholds,
									SupportsReset = true
								},
								new UiBooleanInput
								{
									Key = BatteryWidgetData.ChargingColorKey,
									Label = AppStrings.Widgets.Battery.ChargingColor(),
									Description = AppStrings.Widgets.Battery.ChargingColorHint(),
									Binding = Bind.To(chargingColor)
								}
							]
						}
					},
					new UiWhen
					{
						Key = "single-when",
						Condition = () => colorMode.Value == BatteryWidgetData.ColorSingle,
						Content = () => new UiColorInput
						{
							Key = BatteryWidgetData.ColorKey,
							AllowVariables = true,
							Label = AppStrings.Widgets.Battery.CustomColor(),
							Binding = Bind.To(color),
							SupportsReset = true,
							DefaultValue = string.Empty
						}
					},
					new UiHeading { Key = "appearance-heading", Text = AppStrings.Widgets.Editor.Appearance() },
					WidgetConfigFragments.Background(backgroundColor),
					new UiHeading { Key = "border-heading", Text = AppStrings.Widgets.Editor.Border() },
					WidgetConfigFragments.Border(borderStyle, borderColor, false)
				]
			},
			Editor = WidgetConfigFragments.FlowsEditor(flows)
		};
	}

	private static UiObjectInput DevicePicker(
		IBatteryApi batteries,
		UiState<BatteryDeviceReference?> device,
		UiOptionsState options,
		bool hasDevices)
	{
		var picker = new UiDynamicChoiceInput
		{
			Key = "id",
			Label = AppStrings.Widgets.Battery.Device(),
			Binding = Bind.Custom(() => device.Value?.Id ?? string.Empty,
				value =>
				{
					device.Value = string.IsNullOrEmpty(value) ? null : Pick(batteries, value, device.Peek());
					options.Reload();
				}),
			OptionsState = options
		};

		return new UiObjectInput
		{
			Key = BatteryWidgetData.DeviceKey,
			Binding = Bind.Custom(() => device.Value is { } picked ? ToElement(picked.ToJson()) : _null,
				value => device.Value = BatteryDeviceReference.Read(value)),
			Children = [hasDevices ? picker : picker with { Description = AppStrings.Widgets.Battery.NoDevicesHint() }]
		};
	}

	private static UiObjectInput SelectionPicker(
		IBatteryApi batteries,
		UiState<IReadOnlyList<BatteryDeviceReference>> selection,
		bool hasDevices)
	{
		var picker = new UiMultiSelectInput
		{
			Key = "ids",
			Label = AppStrings.Widgets.Battery.DevicesSelection(),
			Binding = Bind.Custom(() => (IReadOnlyList<string>)[.. selection.Value.Select(reference => reference.Id)],
				ids => selection.Value =
				[
					.. (ids ?? []).Distinct(StringComparer.Ordinal)
					.Select(id => Pick(batteries, id, selection.Peek().FirstOrDefault(reference => reference.Id == id)))
				]),
			Options = UiValue.From(() => (IReadOnlyList<UiOption>)DeviceOptions(batteries, selection.Value))
		};

		return new UiObjectInput
		{
			Key = BatteryWidgetData.SelectionKey,
			Binding = Bind.Custom(() => ToElement(new JsonObject
				{
					[BatteryWidgetData.SelectionDevicesKey] = new JsonArray([
						.. selection.Value.Select(reference => (JsonNode)reference.ToJson())
					])
				}),
				value => selection.Value = BatteryWidgetData.ReadSelection(ToElement(new JsonObject
				{
					[BatteryWidgetData.SelectionKey]
						= JsonNode.Parse(value.ValueKind == JsonValueKind.Undefined ? "null" : value.GetRawText())
				}))),
			Children = [hasDevices ? picker : picker with { Description = AppStrings.Widgets.Battery.NoDevicesHint() }]
		};
	}

	internal static List<UiOption> DeviceOptions(IBatteryApi batteries, IReadOnlyList<BatteryDeviceReference> stored)
	{
		var devices = batteries.Devices;
		var options = devices
			.Select(device => new UiOption
			{
				Value = device.Id,
				Label = device.ProviderName is { Length: > 0 } provider
					? AppStrings.Widgets.Battery.DeviceOption(device.Name, provider)
					: LocalizedText.FromLiteral(device.Name)
			})
			.ToList();

		foreach (var reference in stored)
		{
			if (devices.Any(device => string.Equals(device.Id, reference.Id, StringComparison.Ordinal)))
			{
				continue;
			}

			options.Add(new UiOption
			{
				Value = reference.Id,
				Label = AppStrings.Widgets.Battery.DeviceUnavailable(reference.Name ?? reference.Id)
			});
		}

		return options;
	}

	private static BatteryDeviceReference Refresh(BatteryDeviceReference stored, IBatteryApi batteries)
	{
		return batteries.Find(stored.Id) is { } device ? BatteryDeviceReference.Of(device) : stored;
	}

	private static BatteryDeviceReference Pick(IBatteryApi batteries, string id, BatteryDeviceReference? previous)
	{
		return batteries.Find(id) is { } device
			? BatteryDeviceReference.Of(device)
			: previous is not null && previous.Id == id
				? previous
				: new BatteryDeviceReference(id, null, BatteryDeviceKinds.Other);
	}

	private static JsonElement ToElement(JsonNode node)
	{
		return JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());
	}
}
