using System.Text.Json;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Widgets.Configuration;

/// <summary>
/// The two fragments every built-in widget's configuration tree repeats: the <c>border</c> object every
/// schema declares the same way, and the <c>flows</c> action-list editor every widget but Slider offers.
/// Factored out so the nine border styles and their labels, and the one editor node, are authored once.
/// </summary>
internal static class WidgetConfigFragments
{
	private static readonly IReadOnlyList<UiOption> _borderStyleOptions =
	[
		UiOption.Of("off", AppStrings.Widgets.Appearance.Border.Off()),
		UiOption.Of(UiComponentBorderStyles.Static, AppStrings.Widgets.Appearance.Border.Static()),
		UiOption.Of(UiComponentBorderStyles.Heartbeat, AppStrings.Widgets.Appearance.Border.Heartbeat()),
		UiOption.Of(UiComponentBorderStyles.Breathing, AppStrings.Widgets.Appearance.Border.Breathing()),
		UiOption.Of(UiComponentBorderStyles.Blink, AppStrings.Widgets.Appearance.Border.Blink()),
		UiOption.Of(UiComponentBorderStyles.Comet, AppStrings.Widgets.Appearance.Border.Comet()),
		UiOption.Of(UiComponentBorderStyles.Ants, AppStrings.Widgets.Appearance.Border.MarchingAnts()),
		UiOption.Of(UiComponentBorderStyles.HueShift, AppStrings.Widgets.Appearance.Border.HueShift()),
		UiOption.Of(UiComponentBorderStyles.Rgb, AppStrings.Widgets.Appearance.Border.Rgb()),
	];

	private static readonly IReadOnlyList<string> _colorBorderStyles =
	[
		UiComponentBorderStyles.Static, UiComponentBorderStyles.Heartbeat, UiComponentBorderStyles.Breathing,
		UiComponentBorderStyles.Blink, UiComponentBorderStyles.Comet, UiComponentBorderStyles.Ants,
	];

	/// <summary>The <c>border</c> object: a <c>style</c> choice over the nine styles the widget profile draws
	/// plus "off", and a <c>color</c> the color-tinted styles use. Nested under a <see cref="UiObjectInput" />
	/// so the two children configure <c>border.style</c>/<c>border.color</c>, per ADR 0050.</summary>
	public static UiObjectInput Border(UiState<string> style, UiState<string> color, bool labelled = true)
	{
		ArgumentNullException.ThrowIfNull(style);
		ArgumentNullException.ThrowIfNull(color);

		return Border(Bind.To(style), Bind.To(color), labelled);
	}

	/// <summary>The binding-based counterpart of <see cref="Border(UiState{string},UiState{string})" />, for a
	/// caller whose <c>style</c>/<c>color</c> do not live in their own top-level <see cref="UiState{T}" /> cell -
	/// Action Button's per-state appearance, where the two fields live inside the state's own entry in a
	/// <c>states</c> list.</summary>
	/// <param name="labelled">Whether the group names itself. A caller that already sits under a heading or
	/// a tab saying "Border" passes false, so the word is not read out twice.</param>
	public static UiObjectInput Border(UiBinding<string> style, UiBinding<string> color, bool labelled = true)
	{
		var border = new UiObjectInput
		{
			Key = "border",
			Children =
			[
				new UiChoiceInput
				{
					Key = "style",
					Label = AppStrings.Widgets.Appearance.Border.StyleLabel(),
					Binding = style,
					Options = UiValue.Of(_borderStyleOptions),
				},
				new UiColorInput
				{
					Key = "color",
					Label = AppStrings.Widgets.Appearance.Border.ColorLabel(),
					Binding = color,
					// Reset clears the colour (issue #896); the renderer substitutes the default ring
					// colour for a blank one, so there is no literal for this tree to carry.
					SupportsReset = true,
					DefaultValue = string.Empty,
					// "off" has nothing to colour and the two cycling styles bring their own colours,
					// so the picker is offered for exactly the styles that read this value.
					VisibleWhen = new UiVisibleWhen
					{
						ParameterName = "style",
						Values = _colorBorderStyles,
					},
				},
			],
		};

		// Set rather than blanked: a label the DSL never saw is absent, where an empty one is a text with
		// nothing in it, which is not the same thing to a renderer or to the serializer.
		return labelled ? border with { Label = AppStrings.Widgets.Editor.Border() } : border;
	}

	/// <summary>The widget's specialized region, holding only the press-trigger flows editor - the room every
	/// widget but Slider needs and nothing more.</summary>
	public static UiWidgetEditor FlowsEditor(UiState<JsonElement> flows)
	{
		ArgumentNullException.ThrowIfNull(flows);

		return new UiWidgetEditor
		{
			Key = "editor",
			// CanRun: these are a placed widget's own actions, so the editor may run them against it -
			// the affordance a template being authored against no widget would not get.
			Children = [new UiActionsListEditor { Key = "flows", Binding = Bind.To(flows), CanRun = true }],
		};
	}
}
