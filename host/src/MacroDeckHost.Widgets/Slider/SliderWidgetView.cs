using MacroDeck.Localization;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Components;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Widgets.Slider;

internal static class SliderWidgetView
{
	// The brand accent from ui/angular/projects/desktop-ui/src/styles/_themes.scss, used by the range-bar
	// fallback when no colour is configured - a range bar's colours are literals, never a role, so there is
	// no theme accent for it to fall back on the way the interactive slider's own LevelColor does.
	private const string _defaultAccent = "#2196f3";

	// The retired client's PREVIEW_PERCENTAGE: what an unbound slider showed in the editor.
	private const double _unboundPreviewLevel = 0.65;

	public static UiElement Build(
		SliderWidgetData config,
		UiState<SliderWidgetReadout> state,
		UiResource? icon,
		IReadOnlyList<UiEventHandler> sliderEvents,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius)
	{
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(sliderEvents);

		var children = config.IsVertical
			? BuildVerticalChildren(config, state, icon, sliderEvents)
			: BuildHorizontalChildren(config, state, icon, sliderEvents);

		return new UiStack
		{
			Key = "slider",
			Direction = UiComponentDirections.Vertical,
			Padding = WidgetSafeArea.For(cornerRadius),
			Gap = 0.04,
			Background = ColorValue(config.BackgroundColor),
			Children = children,
		};
	}

	/// <summary>The filled fraction of the track, computed out of a state read so that a value change is a
	/// <c>set-properties</c> patch rather than a structural reconcile.</summary>
	internal static double ComputeLevel(SliderWidgetReadout readout)
		=> readout.Max <= readout.Min
			? 0
			: Math.Clamp((readout.Value - readout.Min) / (readout.Max - readout.Min), 0, 1);

	/// <summary>The track's granularity in the same fraction space as <see cref="ComputeLevel" />, or
	/// <c>null</c> when the range is degenerate, the variable declares no step, or the step would not even
	/// fit once in the range - emitting <c>0</c> or a fraction over <c>1</c> would be as wrong as emitting
	/// nothing.</summary>
	internal static double? ComputeStepFraction(SliderWidgetReadout readout)
	{
		if (readout.Max <= readout.Min || readout.Step <= 0)
		{
			return null;
		}

		var fraction = readout.Step / (readout.Max - readout.Min);

		return fraction > 0 && fraction <= 1 ? fraction : null;
	}

	private static List<UiElement> BuildHorizontalChildren(
		SliderWidgetData config,
		UiState<SliderWidgetReadout> state,
		UiResource? icon,
		IReadOnlyList<UiEventHandler> sliderEvents)
	{
		var hasLead = HasLead(config, icon);
		var hasValue = config.ShowValue;
		var children = new List<UiElement>();

		if (hasLead || hasValue)
		{
			var headerChildren = new List<UiElement>();

			if (hasLead)
			{
				headerChildren.Add(Lead(config, icon));
			}

			if (hasValue)
			{
				headerChildren.Add(ValueText(config, state, "value", UiComponentAlignments.End));
			}

			var justify = hasLead && hasValue ? UiComponentJustify.SpaceBetween
				: hasValue ? UiComponentJustify.End
				: UiComponentJustify.Start;

			children.Add(new UiStack
			{
				Key = "header",
				Direction = UiComponentDirections.Horizontal,
				Align = UiComponentAlignments.Center,
				Justify = justify,
				Children = headerChildren,
			});
		}

		children.Add(Track(config, state, sliderEvents, isVertical: false));

		return children;
	}

	private static List<UiElement> BuildVerticalChildren(
		SliderWidgetData config,
		UiState<SliderWidgetReadout> state,
		UiResource? icon,
		IReadOnlyList<UiEventHandler> sliderEvents)
	{
		var children = new List<UiElement>();

		if (HasLead(config, icon))
		{
			children.Add(new UiStack
			{
				Key = "header",
				Direction = UiComponentDirections.Horizontal,
				Align = UiComponentAlignments.Center,
				Justify = UiComponentJustify.Center,
				Children = [Lead(config, icon)],
			});
		}

		children.Add(Track(config, state, sliderEvents, isVertical: true));

		if (config.ShowValue)
		{
			children.Add(ValueText(config, state, "value", UiComponentAlignments.Center));
		}

		return children;
	}

	private static bool HasLead(SliderWidgetData config, UiResource? icon)
		=> icon is not null || (config.ShowLabel && config.Label is not null);

	private static UiStack Lead(SliderWidgetData config, UiResource? icon)
	{
		var children = new List<UiElement>();

		if (icon is { } resolvedIcon)
		{
			children.Add(new UiImage { Key = "icon", Size = 0.16, Source = UiValue.Of(resolvedIcon) });
		}

		if (config.ShowLabel && config.Label is { } label)
		{
			children.Add(new UiTextRun
			{
				Key = "label",
				Text = UiText.Of(label),
				Size = 0.11,
				MinSize = 0.075,
				Weight = UiComponentTextWeights.Medium,
				Role = UiComponentTextRoles.Secondary,
				Color = ColorValue(config.LabelColor),
			});
		}

		return new UiStack
		{
			Key = "lead",
			Direction = UiComponentDirections.Horizontal,
			Gap = 0.03,
			Align = UiComponentAlignments.Center,
			Children = children,
		};
	}

	private static UiTextRun ValueText(SliderWidgetData config,
		UiState<SliderWidgetReadout> state,
		string key,
		string align)
		=> new()
		{
			Key = key,
			Text = UiText.Optional(() => ValueDisplayText(config, state.Value)),
			Size = 0.12,
			MinSize = 0.08,
			Weight = UiComponentTextWeights.SemiBold,
			Role = UiComponentTextRoles.Primary,
			Align = align,
			Color = ColorValue(config.LabelColor),
		};

	/// <summary>The reading spelled out beside the track, formatted the way the variable's own semantic kind
	/// and unit say it means something - so a seek slider reads <c>03:07</c> rather than <c>187</c>, without
	/// the widget knowing what a duration is.</summary>
	private static LocalizedText ValueDisplayText(SliderWidgetData config, SliderWidgetReadout readout)
	{
		if (!IsBound(config) || !readout.Found)
		{
			return AppStrings.Widgets.Slider.ValueUnavailable();
		}

		var formatted = VariableValueFormatter.Format(readout.Value,
			readout.SemanticKind,
			readout.Unit,
			readout.DecimalPlaces);

		return formatted.Unit.IsEmpty
			? formatted.Value
			: AppStrings.Variables.Format.ValueWithUnit(formatted.Value, formatted.Unit);
	}

	private static UiSlider Track(
		SliderWidgetData config,
		UiState<SliderWidgetReadout> state,
		IReadOnlyList<UiEventHandler> sliderEvents,
		bool isVertical)
	{
		// Thin enough that the thumb reads as a knob sitting on a track: the thumb is 1.5x the thickness
		// across, so a heavier track would swallow it.
		var thickness = isVertical ? UiSize.FromBasis(0.11, 0.35) : UiSize.FromBasis(0.09, 0.4);
		var level = LevelValue(config, state);
		var fallbackColor = config.Color ?? _defaultAccent;

		return new UiSlider
		{
			Key = "track",
			Fill = true,
			Direction = isVertical ? UiValue.Of(UiComponentDirections.Vertical) : UiValue.None<string>(),
			Level = level,
			Step = StepValue(config, state),
			LevelColor = ColorValue(config.Color),
			Thickness = thickness,
			Events = sliderEvents,
			Fallback = new UiRangeBar
			{
				Key = "trackFallback",
				Thickness = thickness,
				Start = 0,
				End = level,
				StartColor = fallbackColor,
				EndColor = fallbackColor,
			},
		};
	}

	private static UiValue<double> LevelValue(SliderWidgetData config, UiState<SliderWidgetReadout> state)
		=> IsBound(config) ? UiValue.From(() => ComputeLevel(state.Value)) : UiValue.Of(_unboundPreviewLevel);

	private static UiValue<double> StepValue(SliderWidgetData config, UiState<SliderWidgetReadout> state)
		=> IsBound(config)
			? UiValue.Optional(()
				=> ComputeStepFraction(state.Value) is { } fraction ? UiValue.Of(fraction) : UiValue.None<double>())
			: UiValue.None<double>();

	/// <summary>Whether the slider follows a variable at all, as opposed to the unbound-preview level every
	/// editor showed before one was chosen.</summary>
	private static bool IsBound(SliderWidgetData config) => config.ValueVariable is not null;

	private static UiValue<string> ColorValue(string? color) =>
		color is { } value ? UiValue.Of(value) : UiValue.None<string>();
}
