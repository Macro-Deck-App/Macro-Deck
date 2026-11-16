using System.Globalization;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Components;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Widgets.ActionButton;

/// <summary>
/// Builds the Action Button's fixed tree: the button node and its label text node are always both
/// present, and every state-dependent value is a lazy closure over <paramref name="activeState" /> - see
/// <see cref="Build" />. This is what turns a state flip into two <c>set-properties</c> patches instead
/// of a structural reconcile.
/// </summary>
internal static class ActionButtonWidgetView
{
	// The retired ActionButtonWidgetComponent's fixed 5px label inset, expressed as a basis fraction
	// (issue's resolved decision: "the 5 px label padding becomes a 0.05 basis fraction").
	private const double LabelPadding = 0.05;
	private const double BaseLabelBoxPadding = 0.03;
	private const double LabelBoxRadius = 0.03;

	public static UiElement Build(
		UiState<ActionButtonWidgetData> config,
		UiState<string?> activeState,
		UiState<string?> labelText,
		UiState<IReadOnlyList<string?>> extraLabelTexts,
		UiState<IReadOnlyDictionary<WidgetIconReference, UiResource>> iconResources,
		UiState<WidgetIconResolution> iconProvider,
		UiResource? imageResource,
		IReadOnlyList<UiEventHandler> events,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius)
	{
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(activeState);
		ArgumentNullException.ThrowIfNull(labelText);
		ArgumentNullException.ThrowIfNull(extraLabelTexts);
		ArgumentNullException.ThrowIfNull(iconResources);
		ArgumentNullException.ThrowIfNull(iconProvider);
		ArgumentNullException.ThrowIfNull(events);

		var (label, labelRow) = LabelChain(string.Empty,
			() => Appearance(config, activeState).PrimaryLabel,
			() => labelText.Value,
			false);

		var labelLayer = new UiLayer
		{
			Key = "labels",
			Fill = UiValue.Of(true),
			Children =
			[
				LabelSlot("Primary", () => Appearance(config, activeState).PrimaryLabel, () => labelText.Value),
				new UiRepeat<int>
				{
					Key = "extraLabels",
					Items = UiValue.From(() =>
						(IReadOnlyList<int>)Enumerable.Range(0, config.Value.ExtraLabelSlots).ToList()),
					KeySelector = slot => slot.ToString(CultureInfo.InvariantCulture),
					Template = (slot, _) => ExtraLabelSlot(slot, config, activeState, extraLabelTexts)
				}
			],
			Fallback = labelRow
		};

		return new UiButton
		{
			Key = "actionButton",
			Padding = WidgetSafeArea.For(cornerRadius),
			Justify = UiValue.From(() => JustifyFor(Appearance(config, activeState).LabelPosition)),
			Background = UiValue.Optional(() =>
			{
				var background = Appearance(config, activeState).BackgroundColor;

				return background is null ? UiValue.None<string>() : UiValue.Of(background);
			}),
			Source = UiValue.Optional(() =>
				Backdrop(config, activeState, iconResources, iconProvider, imageResource).Source),
			Fit = UiValue.Optional(()
				=> BackdropDisplay(config, activeState, iconResources, iconProvider, imageResource) is
					{ Fit: ActionButtonIconDisplay.Cover }
					? UiValue.Of(ActionButtonIconDisplay.Cover)
					: UiValue.None<string>()),
			Zoom = UiValue.Optional(() =>
				BackdropDisplay(config, activeState, iconResources, iconProvider, imageResource) is { } d
				&& d.Zoom != 100
					? UiValue.Of(d.Zoom / 100.0)
					: UiValue.None<double>()),
			OffsetX = UiValue.Optional(() =>
				BackdropDisplay(config, activeState, iconResources, iconProvider, imageResource) is { } d
				&& d.OffsetX != 0
					? UiValue.Of(d.OffsetX / 100.0)
					: UiValue.None<double>()),
			OffsetY = UiValue.Optional(() =>
				BackdropDisplay(config, activeState, iconResources, iconProvider, imageResource) is { } d
				&& d.OffsetY != 0
					? UiValue.Of(d.OffsetY / 100.0)
					: UiValue.None<double>()),
			Opacity = UiValue.Optional(() =>
				BackdropDisplay(config, activeState, iconResources, iconProvider, imageResource) is { } d
				&& d.Opacity != 100
					? UiValue.Of(d.Opacity / 100.0)
					: UiValue.None<double>()),
			Tint = UiValue.Optional(() =>
				Backdrop(config, activeState, iconResources, iconProvider, imageResource).Tint is { } tint
					? UiValue.Of(tint)
					: UiValue.None<string>()),
			BorderStyle = UiValue.Optional(() =>
			{
				var style = Appearance(config, activeState).Border?.Style;

				return style is null ? UiValue.None<string>() : UiValue.Of(style);
			}),
			BorderColor = UiValue.Optional(() =>
			{
				var color = Appearance(config, activeState).Border?.Color;

				return color is null ? UiValue.None<string>() : UiValue.Of(color);
			}),
			Events = events,
			Children =
			[
				new UiWhen
				{
					Key = "labels-flat",
					Condition = () => !config.Value.UsesLabelLayer,
					Content = () => labelRow
				},
				new UiWhen
				{
					Key = "labels-layered",
					Condition = () => config.Value.UsesLabelLayer,
					Content = () => labelLayer
				}
			],
			Fallback = new UiStack
			{
				Key = "actionButtonFallback",
				Padding = UiSize.FromBasis(LabelPadding),
				Justify = UiValue.From(() => JustifyFor(Appearance(config, activeState).LabelPosition)),
				Background = UiValue.Optional(() =>
				{
					var background = Appearance(config, activeState).BackgroundColor;

					return background is null ? UiValue.None<string>() : UiValue.Of(background);
				}),
				Children = [label]
			}
		};
	}

	private static UiTransform ExtraLabelSlot(
		int slot,
		UiState<ActionButtonWidgetData> config,
		UiState<string?> activeState,
		UiState<IReadOnlyList<string?>> extraLabelTexts)
	{
		return LabelSlot(slot.ToString(CultureInfo.InvariantCulture), Resolved, Text);

		ActionButtonResolvedLabel Resolved()
		{
			var labels = Appearance(config, activeState).ExtraLabels;

			return slot < labels.Count ? labels[slot] : ActionButtonResolvedLabel.Empty;
		}

		string? Text()
		{
			var texts = extraLabelTexts.Value;

			return slot < texts.Count ? texts[slot] : null;
		}
	}

	// The outer transform moves the slot by a fraction of the content box and the pin inside the row moves
	// the label back by the same fraction of its own size, so 0 is flush start and 1 is flush end.
	private static UiTransform LabelSlot(string suffix, Func<ActionButtonResolvedLabel> resolved, Func<string?> text)
	{
		var (_, row) = LabelChain(suffix, resolved, text, true);

		return new UiTransform
		{
			Key = $"labelSlot{suffix}",
			OffsetX = Fraction(() => resolved().X / 100.0),
			OffsetY = Fraction(() => resolved().Y / 100.0),
			Children =
			[
				new UiStack
				{
					Key = $"labelAnchor{suffix}",
					Justify = UiValue.Of(UiComponentJustify.Start),
					Children = [row]
				}
			]
		};
	}

	private static UiValue<double> Fraction(Func<double> fraction)
	{
		return UiValue.Optional(()
			=> fraction() is var value && value != 0 ? UiValue.Of(value) : UiValue.None<double>());
	}

	private static (UiTextRun Label, UiStack Row) LabelChain(
		string suffix,
		Func<ActionButtonResolvedLabel> resolved,
		Func<string?> text,
		bool placed)
	{
		var label = new UiTextRun
		{
			Key = $"label{suffix}",
			// The resolved text, never the raw stored label: a template must never reach the wire
			// unrendered. The session patches it from its own label signal handling.
			Text = UiText.Optional(() => text() is { } value ? UiText.Of(value) : UiText.None()),
			Size = UiSize.From(() => UiLength.OfBasis(resolved().FontSizePercent / 100.0)),
			Align = UiValue.From(() => AlignFor(resolved().TextAlign)),
			Color = UiValue.From(() => resolved().Color),
			FontFace = UiValue.Optional(() =>
			{
				var face = resolved().FontFace;

				return face is null ? UiValue.None<string>() : UiValue.Of(face);
			}),
			Wrap = UiValue.Of(true),
			Shadow = UiValue.Optional(() => resolved().Shadow ? UiValue.None<bool>() : UiValue.Of(false)),
			StrokeColor = UiValue.Optional(() =>
				resolved() is { HasOutline: true } outlined
					? UiValue.Of(outlined.OutlineColor!)
					: UiValue.None<string>()),
			StrokeWidth = UiSize.Optional(() =>
				resolved() is { HasOutline: true } outlined
					? UiSize.FromBasis(outlined.OutlineWidth / 100.0)
					: UiSize.None())
		};

		var labelBox = new UiModifier
		{
			Key = $"labelBox{suffix}",
			Padding = UiSize.From(() => UiLength.OfBasis(LabelBoxPadding(resolved()))),
			Background = UiValue.Optional(() =>
				resolved().BoxColor is { } color
					? UiValue.Of(UiBackground.Solid(color))
					: UiValue.None<UiBackground>()),
			Radius = UiSize.From(() => UiLength.OfBasis(resolved().HasBox ? LabelBoxRadius : 0)),
			BorderWidth = UiSize.Optional(() =>
				resolved() is { HasBoxBorder: true } bordered
					? UiSize.FromBasis(bordered.BoxBorderWidth / 100.0)
					: UiSize.None()),
			BorderColor = UiValue.Optional(() =>
				resolved() is { HasBoxBorder: true } bordered
					? UiValue.Of(bordered.BoxBorderColor!)
					: UiValue.None<string>()),
			Fill = placed
				? default
				: UiValue.Optional(() => resolved().HasBox ? UiValue.None<bool>() : UiValue.Of(true)),
			Child = label
		};

		var labelRow = new UiStack
		{
			Key = $"labelRow{suffix}",
			Direction = UiComponentDirections.Horizontal,
			Justify = placed
				? UiValue.Of(UiComponentJustify.Start)
				: UiValue.Optional(() =>
					resolved() is { HasBox: true } boxed
						? UiValue.Of(JustifyForAlign(boxed.TextAlign))
						: UiValue.None<string>()),
			Children =
			[
				placed
					? new UiTransform
					{
						Key = $"labelPin{suffix}",
						OffsetX = Fraction(() => -resolved().X / 100.0),
						OffsetY = Fraction(() => -resolved().Y / 100.0),
						Children = [labelBox]
					}
					: labelBox
			]
		};

		return (label, labelRow);
	}

	// The modifier border is drawn inside the box edge, so the padding grows with it to keep it off the text.
	private static double LabelBoxPadding(ActionButtonResolvedLabel label)
	{
		if (!label.HasBox)
		{
			return 0;
		}

		return BaseLabelBoxPadding + (label.HasBoxBorder ? label.BoxBorderWidth / 100.0 : 0);
	}

	private static ActionButtonResolvedAppearance Appearance(
		UiState<ActionButtonWidgetData> config,
		UiState<string?> activeState)
	{
		return config.Value.Resolve(activeState.Value);
	}

	private readonly record struct ResolvedBackdrop(
		UiValue<UiResource> Source,
		ActionButtonIconDisplay? Display,
		string? Tint = null);

	/// <summary>
	/// An active icon provider (issue #425) takes precedence over the configured icon entirely - the same
	/// image for every state, never falling back to the legacy imageUrl face either, since the provider
	/// answered rather than left the question open. Framing (<see cref="ActionButtonIconDisplay" />) still
	/// comes from the state's own configured <c>iconDisplay</c> regardless of which source supplies the
	/// image: the provider owns which image, the widget owns how it is drawn.
	/// </summary>
	private static ResolvedBackdrop Backdrop(
		UiState<ActionButtonWidgetData> config,
		UiState<string?> activeState,
		UiState<IReadOnlyDictionary<WidgetIconReference, UiResource>> iconResources,
		UiState<WidgetIconResolution> iconProvider,
		UiResource? imageResource)
	{
		var icon = config.Value.ResolveIcon(activeState.Value);
		var display = icon?.Display;

		if (iconProvider.Value.IsActive)
		{
			return iconProvider.Value.Resource is { } providerResource
				? new ResolvedBackdrop(UiValue.Of(providerResource), display)
				: new ResolvedBackdrop(UiValue.None<UiResource>(), display);
		}

		if (icon is not null && iconResources.Value.TryGetValue(icon.Icon, out var iconResource))
		{
			return new ResolvedBackdrop(UiValue.Of(iconResource), display, icon.Color);
		}

		// The legacy imageUrl face is drawn only as a last resort, when no icon resolves at all - never
		// as a substitute for an icon this session simply failed to fetch bytes for.
		if (icon is null && imageResource is not null)
		{
			return new ResolvedBackdrop(UiValue.Of(imageResource), null);
		}

		return new ResolvedBackdrop(UiValue.None<UiResource>(), null);
	}

	private static ActionButtonIconDisplay? BackdropDisplay(
		UiState<ActionButtonWidgetData> config,
		UiState<string?> activeState,
		UiState<IReadOnlyDictionary<WidgetIconReference, UiResource>> iconResources,
		UiState<WidgetIconResolution> iconProvider,
		UiResource? imageResource)
	{
		return Backdrop(config, activeState, iconResources, iconProvider, imageResource).Display;
	}

	private static string JustifyFor(string labelPosition)
	{
		return labelPosition switch
		{
			"top" => UiComponentJustify.Start,
			"bottom" => UiComponentJustify.End,
			_ => UiComponentJustify.Center
		};
	}

	private static string JustifyForAlign(string textAlign)
	{
		return textAlign switch
		{
			"left" => UiComponentJustify.Start,
			"right" => UiComponentJustify.End,
			_ => UiComponentJustify.Center
		};
	}

	private static string AlignFor(string textAlign)
	{
		return textAlign switch
		{
			"left" => UiComponentAlignments.Start,
			"right" => UiComponentAlignments.End,
			_ => UiComponentAlignments.Center
		};
	}
}
