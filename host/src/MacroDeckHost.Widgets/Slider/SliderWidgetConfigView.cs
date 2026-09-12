using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.Slider;

/// <summary>
/// Builds a Slider widget's <c>widget-config</c> tree from its stored data - see ADR 0050.
/// </summary>
internal static class SliderWidgetConfigView
{
	public static UiElement Build(JsonElement data, VariableRegistry variables, Guid? widgetId = null)
	{
		ArgumentNullException.ThrowIfNull(variables);

		var label = new UiState<string>(WidgetConfigJson.ReadString(data, "label") ?? string.Empty);
		var showLabel = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showLabel") ?? true);
		var showValue = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showValue") ?? false);
		// UiIconReferenceInput binds UiIconReference rather than UiIconReference? - an icon is a reference
		// type, so the runtime accepts a null value ("no icon") through it despite the non-nullable
		// annotation; declaring the state as UiIconReference? here would fail to unify with the input's own
		// UiBinding<UiIconReference> at the generic level.
		var icon = new UiState<UiIconReference>(ReadIcon(data)!);
		var orientation = new UiState<string>(WidgetConfigJson.ReadString(data, "orientation") ?? "horizontal");
		var color = new UiState<string>(WidgetConfigJson.ReadString(data, "color") ?? string.Empty);
		var labelColor = new UiState<string>(WidgetConfigJson.ReadString(data, "labelColor") ?? string.Empty);
		var backgroundColor =
			new UiState<string>(WidgetConfigJson.ReadString(data, "backgroundColor") ?? string.Empty);

		var border = WidgetConfigJson.ReadObject(data, "border");
		var borderStyle = new UiState<string>(WidgetConfigJson.ReadString(border, "style") ?? "off");
		var borderColor = new UiState<string>(WidgetConfigJson.ReadString(border, "color") ?? string.Empty);

		var valueVariable = new UiState<string>(WidgetConfigJson.ReadString(data, "valueVariable") ?? string.Empty);
		var min = new UiState<double>(WidgetConfigJson.ReadDouble(data, "min") ?? 0);
		var max = new UiState<double>(WidgetConfigJson.ReadDouble(data, "max") ?? 100);
		var step = new UiState<double>(WidgetConfigJson.ReadDouble(data, "step") ?? 1);
		var flows = new UiState<JsonElement>(WidgetConfigJson.ReadFlows(data));

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading
					{
						Key = "binding-heading", Text = AppStrings.Widgets.Editor.Binding(),
					},
					new UiVariablePickerInput
					{
						Key = "valueVariable",
						Label = AppStrings.Widgets.Slider.ValueVariable(),
						Placeholder = AppStrings.Widgets.Slider.SelectVariablePlaceholder(),
						Description = AppStrings.Widgets.Slider.DefaultVariableDescription(),
						Binding = Bind.To(valueVariable),
						VariableTypes = UiValue.Of<IReadOnlyList<string>>(["numeric"]),
						WritableOnly = true,
					},
					// One row, the way the original binding form laid the three bounds out: each is a
					// short number and a column apiece would push the region three rows taller.
					new UiConfigStack
					{
						Key = "range",
						Direction = "horizontal",
						Children =
						[
							WhenUnbound("min",
								variables,
								widgetId,
								valueVariable,
								entity => entity.Min,
								() => new UiNumberInput
								{
									Key = "min",
									Label = AppStrings.Widgets.Slider.Minimum(),
									Binding = Bind.To(min),
								}),
							WhenUnbound("max",
								variables,
								widgetId,
								valueVariable,
								entity => entity.Max,
								() => new UiNumberInput
								{
									Key = "max",
									Label = AppStrings.Widgets.Slider.Maximum(),
									Binding = Bind.To(max),
								}),
							WhenUnbound("step",
								variables,
								widgetId,
								valueVariable,
								entity => entity.Step,
								() => new UiNumberInput
								{
									Key = "step",
									Label = AppStrings.Widgets.Slider.Step(),
									Binding = Bind.To(step),
								}),
						],
					},
					new UiHeading
					{
						Key = "appearance-heading", Text = AppStrings.Widgets.Editor.Appearance(),
					},
					new UiStringInput
					{
						Key = "label",
						Label = AppStrings.Widgets.Editor.Label(),
						Binding = Bind.To(label),
						LiteralOnly = true,
					},
					new UiBooleanInput
					{
						Key = "showLabel", Label = AppStrings.Widgets.Slider.ShowLabel(), Binding = Bind.To(showLabel),
					},
					new UiBooleanInput
					{
						Key = "showValue", Label = AppStrings.Widgets.Slider.ShowValue(), Binding = Bind.To(showValue),
					},
					new UiIconReferenceInput
						{ Key = "icon", Label = AppStrings.Widgets.Editor.Icon(), Binding = Bind.To(icon) },
					new UiChoiceInput
					{
						Key = "orientation",
						Label = AppStrings.Widgets.Slider.Orientation(),
						Binding = Bind.To(orientation),
						Options = UiValue.Of<IReadOnlyList<UiOption>>([
							UiOption.Of("horizontal", AppStrings.Widgets.Slider.Horizontal()),
							UiOption.Of("vertical", AppStrings.Widgets.Slider.Vertical()),
						]),
					},
					// All three reset to unset rather than to a literal hex (issue #896): each one's real
					// default is a theme colour the view substitutes for a blank value, so clearing keeps
					// the slider following the theme instead of pinning today's accent into the widget.
					new UiColorInput
					{
						Key = "color",
						Label = AppStrings.Widgets.Slider.SliderColor(),
						Binding = Bind.To(color),
						SupportsReset = true,
						DefaultValue = string.Empty,
					},
					new UiColorInput
					{
						Key = "labelColor", Label = AppStrings.Widgets.Editor.LabelColor(),
						Binding = Bind.To(labelColor),
						SupportsReset = true,
						DefaultValue = string.Empty,
					},
					new UiColorInput
					{
						Key = "backgroundColor",
						Label = AppStrings.Widgets.Editor.BackgroundColor(),
						Binding = Bind.To(backgroundColor),
						SupportsReset = true,
						DefaultValue = string.Empty,
					},
					new UiHeading { Key = "border-heading", Text = AppStrings.Widgets.Editor.Border() },
					WidgetConfigFragments.Border(borderStyle, borderColor, labelled: false),
				],
			},
			Editor = new UiWidgetEditor
			{
				Key = "editor",
				Children =
				[
					new UiActionsListEditor
					{
						Key = "flows",
						Binding = Bind.To(flows),
						CanRun = true,
						Triggers = UiValue.Of<IReadOnlyList<string>>([WidgetTriggerTypes.DoublePress]),
					},
				],
			},
		};
	}

	/// <summary>
	/// The field is in the tree while no variable is picked, since the default <c>slider_value</c> declares no
	/// range, and while the picked variable declares no bound of its own for it -
	/// the schema's own per-field rule, that <c>min</c>/<c>max</c>/<c>step</c> are used only where the bound
	/// variable declares no such bound itself.
	///
	/// <para>
	/// Structural rather than <see cref="UiVisibleWhen" />, because this is not a question about another
	/// field's rendered value: it is a lookup into variable metadata only the host can do.
	/// <see cref="UiVisibleWhen" /> compares a sibling's value against a declared list, so expressing it that
	/// way would mean emitting a predicate that reads as "visible when valueVariable equals whatever it
	/// currently is" - true by construction, and meaningless to any renderer reading the tree. Omitting the
	/// node says what is actually meant.
	/// </para>
	/// </summary>
	private static UiWhen WhenUnbound(
		string key,
		VariableRegistry variables,
		Guid? widgetId,
		UiState<string> valueVariable,
		Func<VariableEntity, double?> ownBound,
		Func<UiElement> content)
		=> new()
		{
			Key = $"{key}Bound",
			Condition = () =>
			{
				var name = valueVariable.Value;

				if (string.IsNullOrEmpty(name))
				{
					return true;
				}

				var entity = SliderDefaultVariable.Find(variables, widgetId, name);

				return entity is not null && ownBound(entity) is null;
			},
			Content = content,
		};

	private static UiIconReference? ReadIcon(JsonElement data)
	{
		var icon = data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty("icon", out var iconElement) &&
			iconElement.ValueKind == JsonValueKind.Object
				? JsonNode.Parse(iconElement.GetRawText())
				: null;

		var legacyIconId = WidgetConfigJson.ReadString(data, "iconId");
		var reference = WidgetIconReference.Read(icon, legacyIconId);

		return reference is { } value ? new UiIconReference(value.Type, value.Reference) : null;
	}
}
