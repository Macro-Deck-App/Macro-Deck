using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Localization;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Icons.Included;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.Gauges;

internal static class GaugesWidgetConfigView
{
	public const string CpuVariable = "system_cpu_usage_percent";
	public const string RamVariable = "system_ram_usage_percent";
	public const string GpuVariable = "system_gpu_0_usage_percent";

	private const string None = "none";

	public static UiElement Build(JsonElement data, Func<string, VariableEntity?>? findVariable = null)
	{
		var title = new UiState<string>(WidgetConfigJson.ReadString(data, "title") ?? string.Empty);
		var style = new UiState<string>(WidgetConfigJson.ReadString(data, "style") == GaugesWidgetData.StyleArc
			? GaugesWidgetData.StyleArc
			: GaugesWidgetData.StyleRing);
		var gauges = new UiState<List<JsonObject>>(ReadGauges(data));
		var selected = new UiState<string>(gauges.Value.Count > 0 ? IdOf(gauges.Value[0]) : string.Empty);

		var backgroundColor =
			new UiState<string>(WidgetConfigJson.ReadString(data, "backgroundColor") ?? string.Empty);

		var border = WidgetConfigJson.ReadObject(data, "border");
		var borderStyle = new UiState<string>(WidgetConfigJson.ReadString(border, "style") ?? "off");
		var borderColor = new UiState<string>(WidgetConfigJson.ReadString(border, "color") ?? string.Empty);

		var flows = new UiState<JsonElement>(WidgetConfigJson.ReadFlows(data));

		bool HasRoom() => gauges.Value.Count < GaugesWidgetData.MaxGauges;

		string Selected()
			=> gauges.Value.Any(gauge => IdOf(gauge) == selected.Value)
				? selected.Value
				: gauges.Value.Count > 0
					? IdOf(gauges.Value[0])
					: string.Empty;

		void Append(JsonObject gauge)
		{
			if (!HasRoom())
			{
				return;
			}

			var id = NewId();
			gauge["id"] = id;
			gauges.Value = [.. gauges.Value, gauge];
			selected.Value = id;
		}

		void RemoveSelected()
		{
			var id = Selected();
			var index = gauges.Value.FindIndex(gauge => IdOf(gauge) == id);

			if (index < 0)
			{
				return;
			}

			var remaining = gauges.Value.Where((_, position) => position != index).ToList();
			gauges.Value = remaining;
			selected.Value = remaining.Count > 0 ? IdOf(remaining[Math.Min(index, remaining.Count - 1)]) : string.Empty;
		}

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiStringInput
					{
						Key = "title",
						Label = AppStrings.Widgets.Editor.Title(),
						Binding = Bind.To(title),
						LiteralOnly = true,
					},
					new UiChoiceInput
					{
						Key = "style",
						Label = AppStrings.Widgets.Editor.Style(),
						Binding = Bind.To(style),
						Options = UiValue.Of<IReadOnlyList<UiOption>>([
							UiOption.Of(GaugesWidgetData.StyleRing, AppStrings.Widgets.Gauges.StyleRing()),
							UiOption.Of(GaugesWidgetData.StyleArc, AppStrings.Widgets.Gauges.StyleArc()),
						]),
					},
					new UiHeading { Key = "presets-heading", Text = AppStrings.Widgets.History.Presets() },
					new UiWhen
					{
						Key = "presets-when",
						Condition = HasRoom,
						Content = () => new UiConfigStack
						{
							Key = "presets",
							Direction = "horizontal",
							Children =
							[
								PresetButton("presetCpu",
									AppStrings.Widgets.History.PresetCpu(),
									() => Append(Preset(CpuVariable, "CPU", IncludedIconPack.Cpu))),
								PresetButton("presetRam",
									AppStrings.Widgets.History.PresetRamPercent(),
									() => Append(Preset(RamVariable, "RAM", IncludedIconPack.MemoryStick))),
								PresetButton("presetGpu",
									AppStrings.Widgets.History.PresetGpu(),
									() => Append(Preset(GpuVariable, "GPU", IncludedIconPack.Gpu))),
							],
						},
					},
					new UiWhen
					{
						Key = "full-when",
						Condition = () => !HasRoom(),
						Content = () => new UiProse { Key = "full-hint", Text = AppStrings.Widgets.Gauges.FullHint() },
					},
					new UiHeading { Key = "gauges-heading", Text = AppStrings.Widgets.Gauges.ListHeading() },
					new UiConfigStack
					{
						Key = "gauge-row",
						Direction = "horizontal",
						Wrap = false,
						Children =
						[
							new UiWhen
							{
								Key = "selectedGauge-when",
								Condition = () => gauges.Value.Count > 0,
								Content = () => new UiChoiceInput
								{
									Key = "selectedGauge",
									Label = AppStrings.Widgets.Gauges.ListHeading(),
									HideLabel = true,
									RowWeight = 1,
									Transient = true,
									Binding = Bind.Custom(Selected, value => selected.Value = value),
									Options = UiValue.From(() => (IReadOnlyList<UiOption>)gauges.Value
										.Select((gauge, index) => UiOption.Of(IdOf(gauge), LabelOf(gauge, index)))
										.ToList()),
								},
							},
							new UiWhen
							{
								Key = "addGauge-when",
								Condition = HasRoom,
								Content = () => new UiConfigButton
								{
									Key = "addGauge",
									Label = AppStrings.Widgets.Gauges.AddGauge(),
									Icon = "plus",
									Events = [UiEventHandler.On(UiConfigEvents.Activate, () => Append(new JsonObject()))],
								},
							},
							new UiWhen
							{
								Key = "deleteGauge-when",
								Condition = () => gauges.Value.Count > 0,
								Content = () => new UiConfigButton
								{
									Key = "deleteGauge",
									Label = MacroDeckStrings.Common.Delete(),
									Icon = "trash",
									Events = [UiEventHandler.On(UiConfigEvents.Activate, RemoveSelected)],
								},
							},
						],
					},
					new UiArrayInput
					{
						Key = "gauges",
						Binding = Bind.Custom(() => Serialize(gauges.Value), value => gauges.Value = Deserialize(value)),
						Children =
						[
							new UiRepeat<JsonObject>
							{
								Key = "gaugeItems",
								Items = UiValue.From(() => (IReadOnlyList<JsonObject>)gauges.Value),
								KeySelector = IdOf,
								Template = (gauge, _) => Item(gauges, IdOf(gauge), () => Selected() == IdOf(gauge), findVariable),
							},
						],
					},
					new UiHeading { Key = "appearance-heading", Text = AppStrings.Widgets.Editor.Appearance() },
					WidgetConfigFragments.Background(backgroundColor),
					new UiHeading { Key = "border-heading", Text = AppStrings.Widgets.Editor.Border() },
					WidgetConfigFragments.Border(borderStyle, borderColor, labelled: false),
				],
			},
			Editor = WidgetConfigFragments.FlowsEditor(flows),
		};
	}

	private static UiObjectInput Item(UiState<List<JsonObject>> gauges,
		string id,
		Func<bool> isSelected,
		Func<string, VariableEntity?>? findVariable)
		=> new()
		{
			Key = id,
			Children =
			[
				new UiWhen
				{
					Key = $"selected-when-{id}",
					Condition = isSelected,
					Content = () => new UiFragment { Key = $"fields-{id}", Children = Fields(gauges, id, findVariable) },
				},
			],
		};

	private static UiElement[] Fields(UiState<List<JsonObject>> gauges,
		string id,
		Func<string, VariableEntity?>? findVariable)
		=>
			[
				new UiVariablePickerInput
				{
					Key = "variable",
					Label = AppStrings.Widgets.Slider.ValueVariable(),
					Placeholder = AppStrings.Widgets.History.ValueVariablePlaceholder(),
					Binding = StringField(gauges, id, "variable"),
					VariableTypes = UiValue.Of<IReadOnlyList<string>>(["numeric"]),
				},
				new UiStringInput
				{
					Key = "name",
					Label = AppStrings.Widgets.Gauges.Name(),
					Placeholder = AppStrings.Widgets.Gauges.NamePlaceholder(),
					Description = AppStrings.Widgets.Gauges.NameHint(),
					Binding = StringField(gauges, id, "name"),
				},
				new UiIconReferenceInput
				{
					Key = "icon",
					Label = AppStrings.Widgets.Editor.Icon(),
					Binding = IconField(gauges, id),
				},
				new UiColorInput
				{
					Key = "iconColor",
					AllowVariables = true,
					Label = AppStrings.Widgets.Editor.IconColor(),
					Binding = StringField(gauges, id, "iconColor"),
					SupportsReset = true,
					DefaultValue = string.Empty,
				},
				new UiNumberInput
				{
					Key = "min",
					Label = AppStrings.Widgets.Slider.Minimum(),
					Binding = NumberField(gauges, id, "min"),
				},
				new UiNumberInput
				{
					Key = "max",
					Label = AppStrings.Widgets.Slider.Maximum(),
					Description = AppStrings.Widgets.Gauges.MaximumHint(),
					Binding = NumberField(gauges, id, "max"),
				},
				new UiColorInput
				{
					Key = "color",
					AllowVariables = true,
					Label = AppStrings.Widgets.Gauges.Color(),
					Binding = StringField(gauges, id, "color"),
					SupportsReset = true,
					DefaultValue = string.Empty,
				},
				new UiBooleanInput
				{
					Key = WidgetThresholds.EnabledKey,
					Label = AppStrings.Widgets.Editor.ColorThresholds(),
					Description = AppStrings.Widgets.Gauges.ColorThresholdsDescription(),
					Binding = FlagField(gauges, id, WidgetThresholds.EnabledKey),
				},
				new UiWhen
				{
					Key = $"thresholds-when-{id}",
					Condition = () => ThresholdsOn(gauges, id),
					Content = () => new UiThresholdsInput
					{
						Key = WidgetThresholds.ValueKey,
						AllowVariables = true,
						Label = AppStrings.Widgets.Editor.ColorThresholds(),
						HideLabel = true,
						Binding = ThresholdsField(gauges, id),
						Min = UiValue.From(() => Scale(gauges, id, findVariable).Min),
						Max = UiValue.From(() => Scale(gauges, id, findVariable).Max),
						Unit = UiText.Optional(() => Picked(gauges, id, findVariable)?.Unit is { Length: > 0 } unit
							? VariableValueFormatter.Format(0, null, unit, null).Unit
							: UiText.None()),
						DefaultValue = UiValue.From(() =>
						{
							var (min, max) = Scale(gauges, id, findVariable);

							return WidgetThresholds.Defaults(min, max);
						}),
						SupportsReset = true,
					},
				},
				new UiWhen
				{
					Key = $"warnWhen-when-{id}",
					Condition = () => !ThresholdsOn(gauges, id),
					Content = () => new UiChoiceInput
					{
						Key = "warnWhen",
						Label = AppStrings.Widgets.Gauges.Warning(),
						Description = AppStrings.Widgets.Gauges.WarningHint(),
						Binding = StringField(gauges, id, "warnWhen", None),
						Options = UiValue.Of<IReadOnlyList<UiOption>>([
							UiOption.Of(None, AppStrings.Widgets.Gauges.WarningOff()),
							UiOption.Of(GaugeConfig.WarnAbove, AppStrings.Widgets.Gauges.WarningAbove()),
							UiOption.Of(GaugeConfig.WarnBelow, AppStrings.Widgets.Gauges.WarningBelow()),
						]),
					},
				},
				new UiWhen
				{
					Key = $"warnAt-when-{id}",
					Condition = () => !ThresholdsOn(gauges, id) && Find(gauges.Value, id)?["warnWhen"] is JsonValue,
					Content = () => new UiNumberInput
					{
						Key = "warnAt",
						Label = AppStrings.Widgets.Gauges.Threshold(),
						Binding = NumberField(gauges, id, "warnAt"),
					},
				},
			];

	private static LocalizedText LabelOf(JsonObject gauge, int index)
		=> gauge["name"] is JsonValue name && name.TryGetValue<string>(out var text) && text.Length > 0
			? LocalizedText.FromLiteral(text)
			: AppStrings.Widgets.Gauges.GaugeNumber(
				number: LocalizedText.FromLiteral((index + 1).ToString(CultureInfo.InvariantCulture)));

	private static UiConfigButton PresetButton(string key, LocalizedString label, Action apply)
		=> new()
		{
			Key = key,
			Label = label,
			Events = [UiEventHandler.On(UiConfigEvents.Activate, apply)],
		};

	internal static JsonObject Preset(string variable, string name, string icon)
		=> new()
		{
			["variable"] = variable,
			["name"] = name,
			["icon"] = WidgetIconReference.IconPack(IncludedIconPack.IconId(icon).ToString()).ToJson(),
			["max"] = 100,
		};

	private static bool ThresholdsOn(UiState<List<JsonObject>> gauges, string id)
		=> Find(gauges.Value, id)?[WidgetThresholds.EnabledKey] is JsonValue value &&
			value.TryGetValue<bool>(out var enabled) &&
			enabled;

	private static VariableEntity? Picked(UiState<List<JsonObject>> gauges,
		string id,
		Func<string, VariableEntity?>? findVariable)
		=> findVariable is not null &&
			Find(gauges.Value, id)?["variable"] is JsonValue value &&
			value.TryGetValue<string>(out var name) &&
			name.Length > 0
				? findVariable(name)
				: null;

	private static (double Min, double Max) Scale(UiState<List<JsonObject>> gauges,
		string id,
		Func<string, VariableEntity?>? findVariable)
		=> Find(gauges.Value, id) is { } gauge
			? GaugesViewStateResolver.Bounds(
				GaugeConfig.Parse(JsonSerializer.Deserialize<JsonElement>(gauge.ToJsonString()), id),
				Picked(gauges, id, findVariable))
			: (0, 100);

	private static UiBinding<bool> FlagField(UiState<List<JsonObject>> gauges, string id, string field)
		=> Bind.Custom(() => Find(gauges.Value, id)?[field] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag,
			value => Mutate(gauges,
				id,
				gauge =>
				{
					if (value)
					{
						gauge[field] = true;
					}
					else
					{
						gauge.Remove(field);
					}
				}));

	private static UiBinding<UiThresholds> ThresholdsField(UiState<List<JsonObject>> gauges, string id)
		=> Bind.Custom(() => (Find(gauges.Value, id)?[WidgetThresholds.ValueKey] is JsonObject stored &&
				UiThresholds.TryParse(JsonSerializer.Deserialize<JsonElement>(stored.ToJsonString()), out var parsed)
					? parsed
					: null)!,
			value => Mutate(gauges,
				id,
				gauge =>
				{
					if (value is null)
					{
						gauge.Remove(WidgetThresholds.ValueKey);
					}
					else
					{
						gauge[WidgetThresholds.ValueKey] = JsonNode.Parse(UiCanonicalJson.Serialize(value));
					}
				}));

	private static string NewId() => Guid.NewGuid().ToString("N");

	private static string IdOf(JsonObject gauge) => gauge["id"]?.GetValue<string>() ?? string.Empty;

	private static JsonObject? Find(IReadOnlyList<JsonObject> gauges, string id)
		=> gauges.FirstOrDefault(gauge => IdOf(gauge) == id);

	private static void Mutate(UiState<List<JsonObject>> gauges, string id, Action<JsonObject> mutate)
		=> gauges.Value = gauges.Value
			.Select(gauge =>
			{
				if (IdOf(gauge) != id)
				{
					return gauge;
				}

				var copy = (JsonObject)gauge.DeepClone();
				mutate(copy);

				return copy;
			})
			.ToList();

	private static UiBinding<string> StringField(UiState<List<JsonObject>> gauges,
		string id,
		string field,
		string? unset = null)
		=> Bind.Custom(() => Find(gauges.Value, id)?[field] is JsonValue value && value.TryGetValue<string>(out var text)
				? text
				: unset ?? string.Empty,
			value => Mutate(gauges,
				id,
				gauge =>
				{
					if (string.IsNullOrEmpty(value) || value == unset)
					{
						gauge.Remove(field);
					}
					else
					{
						gauge[field] = value;
					}
				}));

	private static UiBinding<double> NumberField(UiState<List<JsonObject>> gauges, string id, string field)
		=> Bind.Custom(() => Find(gauges.Value, id)?[field] is JsonValue value && value.TryGetValue<double>(out var number)
				? number
				: 0,
			value => Mutate(gauges,
				id,
				gauge =>
				{
					if (double.IsFinite(value))
					{
						gauge[field] = value;
					}
				}));

	private static UiBinding<UiIconReference> IconField(UiState<List<JsonObject>> gauges, string id)
		=> Bind.Custom(() =>
			{
				var reference = WidgetIconReference.Read(Find(gauges.Value, id)?["icon"], null);

				return (reference is { } icon ? new UiIconReference(icon.Type, icon.Reference) : null)!;
			},
			value => Mutate(gauges,
				id,
				gauge =>
				{
					if (value is null)
					{
						gauge.Remove("icon");
					}
					else
					{
						gauge["icon"] = new JsonObject { ["type"] = value.Type, ["reference"] = value.Reference };
					}
				}));

	internal static List<JsonObject> ReadGauges(JsonElement data)
	{
		var stored = data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty("gauges", out var gauges) &&
			gauges.ValueKind == JsonValueKind.Array
				? gauges
				: default;

		if (stored.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var ids = GaugesWidgetData.ParseGauges(stored).Select(gauge => gauge.Id).ToList();
		var result = new List<JsonObject>();

		foreach (var item in stored.EnumerateArray())
		{
			if (JsonNode.Parse(item.GetRawText()) is not JsonObject gauge)
			{
				continue;
			}

			gauge["id"] = ids[result.Count];
			result.Add(gauge);
		}

		return result;
	}

	private static JsonElement Serialize(IReadOnlyList<JsonObject> gauges)
		=> JsonSerializer.Deserialize<JsonElement>(new JsonArray([.. gauges.Select(gauge => gauge.DeepClone())])
			.ToJsonString());

	private static List<JsonObject> Deserialize(JsonElement value)
		=> value.ValueKind == JsonValueKind.Array
			? ReadGauges(JsonSerializer.Deserialize<JsonElement>(new JsonObject
				{ ["gauges"] = JsonNode.Parse(value.GetRawText()) }.ToJsonString()))
			: [];
}
