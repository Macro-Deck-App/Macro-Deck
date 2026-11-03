using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;
using MacroDeckHost.Integrations.Ping.State;

namespace MacroDeckHost.Integrations.Ping.Widgets;

internal static class PingWidgetView
{
	public const string Root = "ping";

	private const string OnlineColor = "#68bc71";
	private const string OfflineColor = "#e5534b";
	private const string UnknownColor = "#8b8b8b";

	private static readonly UiLength _titleText = UiLength.Capped(0.12, 14);
	private static readonly UiLength _valueText = UiLength.Capped(0.2, 24);
	private static readonly UiLength _text = UiLength.Capped(0.095, 11.5);
	private static readonly UiLength _smallText = UiLength.Capped(0.08, 9.5);
	private static readonly UiLength _gap = UiLength.Capped(0.035, 4);

	public static UiElement Build(
		UiState<PingViewState> state,
		int cornerRadius = UiWidgetSafeArea.DefaultCornerRadius,
		string? backgroundColor = null)
	{
		ArgumentNullException.ThrowIfNull(state);

		var content = new UiStack
		{
			Key = "content",
			Direction = UiComponentDirections.Vertical,
			Padding = UiWidgetSafeArea.For(cornerRadius),
			Gap = _gap,
			Children =
			[
				Header(state),
				new UiTextRun
				{
					Key = "value",
					Text = UiText.Optional(() => state.Value.Value),
					Size = _valueText,
					MinSize = _text,
					Weight = UiComponentTextWeights.SemiBold,
					Role = UiComponentTextRoles.Primary,
					MaxLines = 1
				},
				new UiTextRun
				{
					Key = "detail",
					Text = UiText.Optional(() => state.Value.Detail),
					Size = _smallText,
					Role = UiComponentTextRoles.Muted,
					MaxLines = 2
				}
			]
		};

		var layer = new UiLayer
		{
			Key = Root,
			Fallback = content,
			Children =
			[
				new UiWhen
				{
					Key = "chartGate",
					Condition = () => state.Value.HasChart,
					Content = () => new UiChart
					{
						Key = "chart",
						Points = UiValue.From(() => state.Value.Points),
						Color = UiValue.From(() => StatusColor(state.Value)),
						PlotTop = 0.62,
						Thickness = UiSize.Capped(2d / UiLength.Cell, 2.5),
						Fallback = new UiTextRun { Key = "chartFallback", Text = UiText.Of(string.Empty) }
					}
				},
				content
			]
		};

		return backgroundColor is { } background
			? new UiStack { Key = "pingBackground", Background = background, Children = [layer with { Fill = true }] }
			: layer;
	}

	internal static string StatusColor(PingViewState state)
	{
		return state.Reachability switch
		{
			PingReachability.Online => OnlineColor,
			PingReachability.Offline => OfflineColor,
			_ => UnknownColor
		};
	}

	private static UiStack Header(UiState<PingViewState> state)
	{
		return new UiStack
		{
			Key = "header",
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			Gap = _gap,
			Children =
			[
				new UiModifier
				{
					Key = "statusDot",
					Frame = new UiFrame { Width = _smallText, Height = _smallText },
					Child = new UiShape
					{
						Key = "shape",
						Shape = UiComponentShapes.Circle,
						Color = UiValue.From(() => StatusColor(state.Value))
					}
				},
				new UiTextRun
				{
					Key = "title",
					Text = UiText.From(() => state.Value.Title),
					Size = _titleText,
					MinSize = _text,
					Weight = UiComponentTextWeights.SemiBold,
					Role = UiComponentTextRoles.Primary,
					MaxLines = 1,
					Fill = true
				}
			]
		};
	}
}
