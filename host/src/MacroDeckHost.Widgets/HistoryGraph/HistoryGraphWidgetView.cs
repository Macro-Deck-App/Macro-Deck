using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Components;

namespace MacroDeckHost.Widgets.HistoryGraph;

/// <summary>
/// The History Graph card: labels pinned to the top, the value centred on the whole card, and the chart
/// full-bleed across a band at its foot.
///
/// <para>
/// Three layers rather than a column, because none of the three is positioned by the other two: the value
/// is centred on the <i>card</i>, not on whatever the labels leave over, and the chart bleeds to all four
/// edges while the labels keep the card's padding. A column would tie all three together.
/// </para>
///
/// <para>
/// Every size is capped as well as scaled (see <see cref="UiLength.MaxOfCell" />): the type on this
/// card stays the size it is whether the widget is one cell or nine, so a large graph reads as a large
/// chart with a caption rather than as a poster.
/// </para>
/// </summary>
internal static class HistoryGraphWidgetView
{
	/// <summary>The chart's band, as a fraction of the card's height. The value's own size is bounded, so
	/// it clears this band at every widget size.</summary>
	private const double ChartTop = 0.66;

	private static readonly UiSize _labelGap = UiSize.Capped(0.01, 4);
	private static readonly UiSize _valueGap = UiSize.Capped(0.01, 3);
	private static readonly UiSize _titleSize = UiSize.Capped(0.11, 13);
	private static readonly UiSize _subtitleSize = UiSize.Capped(0.08, 11);
	private static readonly UiSize _valueSize = UiSize.Capped(0.26, 34);
	private static readonly UiSize _unitSize = UiSize.Capped(0.12, 14.5);

	// A hairline that stays a hairline: capped at two reference pixels, so the line does not thicken with
	// the card the way its type deliberately does not grow with it either.
	private static readonly UiSize _lineThickness = UiSize.Capped(2d / UiLength.Cell, 2);

	public static UiElement Build(UiState<HistoryGraphViewState> state,
		HistoryGraphWidgetData config,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(config);

		// The graph itself stays edge to edge - it is the tile's background, not its content - so the
		// clearance applies to the labels laid over it. See ADR 0064.
		var safeArea = WidgetSafeArea.For(cornerRadius);

		// Built once and shared between the layer and its fallback, the way MusicPlayerWidgetView shares
		// its rows: an element is an immutable description, and only one of the two is ever drawn.
		var value = Value(state);

		return new UiLayer
		{
			Key = "historyGraph",
			Children = [Chart(state, config), Labels(state, config, safeArea), value],

			// An older reader that cannot layer draws the card's substance - the value - rather than a
			// placeholder: the labels and the chart are context for a number, not the point of the widget.
			Fallback = value,
		};
	}

	private static UiChart Chart(UiState<HistoryGraphViewState> state, HistoryGraphWidgetData config)
		=> new()
		{
			Key = "chart",
			Points = UiValue.From(() => state.Value.Points),
			Color = config.AccentColor is { } accent ? UiValue.Of(accent) : UiValue.None<string>(),
			PlotTop = ChartTop,
			Thickness = _lineThickness,

			// Nothing at all rather than a placeholder: an older reader still shows the value and the
			// labels above, which is the card minus its chart rather than a card that looks broken.
			Fallback = Nothing("chartFallback"),
		};

	private static UiStack Labels(UiState<HistoryGraphViewState> state,
		HistoryGraphWidgetData config,
		UiSize safeArea)
	{
		var children = new List<UiElement>();

		if (!string.IsNullOrEmpty(config.Title))
		{
			children.Add(new UiTextRun
			{
				Key = "title",
				Text = UiText.Of(config.Title),
				Size = _titleSize,
				Weight = UiComponentTextWeights.SemiBold,
				Role = UiComponentTextRoles.Primary,
			});
		}

		// A gate rather than an empty run: a subtitle bound to a variable can stop resolving at any moment,
		// and a run left in place would still take its own height and the gap above it.
		children.Add(new UiWhen
		{
			Key = "subtitleGate",
			Condition = () => state.Value.Subtitle.Length > 0,
			Content = () => new UiTextRun
			{
				Key = "subtitle",
				Text = UiText.From(() => state.Value.Subtitle),
				Size = _subtitleSize,
				Role = UiComponentTextRoles.Muted,
			},
		});

		return new UiStack
		{
			Key = "labels",
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Start,
			Align = UiComponentAlignments.Start,
			Gap = _labelGap,
			Padding = safeArea,
			Children = children,
		};
	}

	private static UiStack Value(UiState<HistoryGraphViewState> state)
	{
		var children = new List<UiElement>
		{
			new UiTextRun
			{
				Key = "value",
				Text = UiText.From(() => state.Value.Value),
				Size = _valueSize,
				Weight = UiComponentTextWeights.Bold,
				Role = UiComponentTextRoles.Primary,
				Align = UiComponentAlignments.Center,
				Digits = UiValue.From(() => state.Value.Digits),
			},

			// A gate rather than a decision taken while the tree is built: the unit now comes from the bound
			// variable, so a rebind onto one that declares none has to be able to take the node away again.
			new UiWhen
			{
				Key = "unitGate",
				Condition = () => state.Value.HasUnit,
				Content = () => new UiTextRun
				{
					Key = "unit",
					Text = UiText.Optional(() => state.Value.Unit),
					Size = _unitSize,
					Weight = UiComponentTextWeights.SemiBold,
					Role = UiComponentTextRoles.Secondary,
				},
			},
		};

		// No padding: the value is centred on the card and spans its full width, so a long reading uses the
		// whole card rather than stopping at the labels' inset.
		return new UiStack
		{
			Key = "valueLayer",
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Children =
			[
				new UiStack
				{
					Key = "valueRow",
					Direction = UiComponentDirections.Horizontal,
					Justify = UiComponentJustify.Center,
					Align = UiComponentAlignments.Baseline,
					Gap = _valueGap,
					Children = children,
				},
			],
		};
	}

	private static UiTextRun Nothing(string key) => new() { Key = key, Text = UiText.Of(string.Empty) };
}
