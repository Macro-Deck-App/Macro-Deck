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
	private const double _labelPadding = 0.05;

	public static UiElement Build(
		UiState<ActionButtonWidgetData> config,
		UiState<string?> activeState,
		UiState<string?> labelText,
		UiState<IReadOnlyDictionary<WidgetIconReference, UiResource>> iconResources,
		UiState<WidgetIconResolution> iconProvider,
		UiResource? imageResource,
		IReadOnlyList<UiEventHandler> events,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius)
	{
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(activeState);
		ArgumentNullException.ThrowIfNull(labelText);
		ArgumentNullException.ThrowIfNull(iconResources);
		ArgumentNullException.ThrowIfNull(iconProvider);
		ArgumentNullException.ThrowIfNull(events);

		var label = new UiTextRun
		{
			Key = "label",
			// The resolved text, never the raw stored label: a Liquid template must never reach the wire
			// unrendered (host-side resolution is what gap 2 requires), and this is patched by the
			// session's own label signal handling rather than recomputed here.
			Text = UiText.Optional(() => labelText.Value is { } text ? UiText.Of(text) : UiText.None()),
			Size = UiSize.From(() =>
				UiLength.OfBasis(Appearance(config, activeState).FontSizePercent / 100.0)),
			Align = UiValue.From(() => AlignFor(Appearance(config, activeState).TextAlign)),
			Color = UiValue.From(() => Appearance(config, activeState).LabelColor),
			FontFace = UiValue.Optional(() =>
			{
				var face = Appearance(config, activeState).FontFace;

				return face is null ? UiValue.None<string>() : UiValue.Of(face);
			}),
			// The retired ActionButtonWidgetComponent's label wrapped freely (white-space: pre-wrap;
			// overflow-wrap: anywhere; no line clamp) rather than the renderer's single-line + ellipsis
			// default - MaxLines is deliberately left unset so `wrap: true` alone resolves to the
			// renderer's uncapped line count, matching that parity exactly.
			Wrap = UiValue.Of(true),
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
				BackdropDisplay(config, activeState, iconResources, iconProvider, imageResource) is { } d &&
				d.Zoom != 100
					? UiValue.Of(d.Zoom / 100.0)
					: UiValue.None<double>()),
			OffsetX = UiValue.Optional(() =>
				BackdropDisplay(config, activeState, iconResources, iconProvider, imageResource) is { } d &&
				d.OffsetX != 0
					? UiValue.Of(d.OffsetX / 100.0)
					: UiValue.None<double>()),
			OffsetY = UiValue.Optional(() =>
				BackdropDisplay(config, activeState, iconResources, iconProvider, imageResource) is { } d &&
				d.OffsetY != 0
					? UiValue.Of(d.OffsetY / 100.0)
					: UiValue.None<double>()),
			Opacity = UiValue.Optional(() =>
				BackdropDisplay(config, activeState, iconResources, iconProvider, imageResource) is { } d &&
				d.Opacity != 100
					? UiValue.Of(d.Opacity / 100.0)
					: UiValue.None<double>()),
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
			Children = [label],
			Fallback = new UiStack
			{
				Key = "actionButtonFallback",
				Padding = UiSize.FromBasis(_labelPadding),
				Justify = UiValue.From(() => JustifyFor(Appearance(config, activeState).LabelPosition)),
				Background = UiValue.Optional(() =>
				{
					var background = Appearance(config, activeState).BackgroundColor;

					return background is null ? UiValue.None<string>() : UiValue.Of(background);
				}),
				Children = [label],
			},
		};
	}

	private static ActionButtonResolvedAppearance Appearance(UiState<ActionButtonWidgetData> config,
		UiState<string?> activeState)
		=> config.Value.Resolve(activeState.Value);

	private readonly record struct ResolvedBackdrop(UiValue<UiResource> Source, ActionButtonIconDisplay? Display);

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
			return new ResolvedBackdrop(UiValue.Of(iconResource), display);
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
		=> Backdrop(config, activeState, iconResources, iconProvider, imageResource).Display;

	private static string JustifyFor(string labelPosition) => labelPosition switch
	{
		"top" => UiComponentJustify.Start,
		"bottom" => UiComponentJustify.End,
		_ => UiComponentJustify.Center,
	};

	private static string AlignFor(string textAlign) => textAlign switch
	{
		"left" => UiComponentAlignments.Start,
		"right" => UiComponentAlignments.End,
		_ => UiComponentAlignments.Center,
	};
}
