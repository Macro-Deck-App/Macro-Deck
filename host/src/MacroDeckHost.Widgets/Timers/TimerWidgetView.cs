using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Timers;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Widgets.Timers;

internal static class TimerWidgetView
{
	private const string _pausedColor = "#8a8a8a";
	private const string _alertColor = "#c62828";
	private const string _alertTextColor = "#ffffff";

	public static UiElement Build(UiState<TimerFace> face,
		TimerWidgetSettings settings,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius,
		IReadOnlyList<UiEventHandler>? events = null)
	{
		ArgumentNullException.ThrowIfNull(face);
		ArgumentNullException.ThrowIfNull(settings);

		var safeArea = WidgetSafeArea.For(cornerRadius);

		return new UiStack
		{
			Key = "timer",
			Fill = true,
			Background = Tint(settings.BackgroundColor),
			Events = events ?? [],
			Children =
			[
				new UiWhen
				{
					Key = "finishedGate",
					Condition = () => face.Value.IsFinished,
					Content = () => Finished(settings, safeArea),
				},
				new UiWhen
				{
					Key = "clockGate",
					Condition = () => !face.Value.IsFinished,
					Content = () => new UiLayer
					{
						Key = "clockLayer",
						Fill = true,
						Children = [Ring(face, settings, safeArea), Readout(face, settings, safeArea)],
					},
				},
			],
		};
	}

	private static UiStack Ring(UiState<TimerFace> face, TimerWidgetSettings settings, UiSize safeArea)
	{
		var fullTurn = face.Value.Kind == TimerWidgetKind.Stopwatch;

		return new UiStack
		{
			Key = "ringFrame",
			Fill = true,
			Padding = safeArea,
			Children =
			[
				new UiGauge
				{
					Key = "ring",
					Fill = true,
					Thickness = 0.07,
					StartAngle = fullTurn ? UiValue.Of(0d) : UiValue.None<double>(),
					EndAngle = fullTurn ? UiValue.Of(360d) : UiValue.None<double>(),
					Level = UiValue.From(() => face.Value.Level),
					LevelColor = UiValue.Optional(() => face.Value.IsPaused
						? UiValue.Of(_pausedColor)
						: Tint(settings.AccentColor)),
					Fallback = new UiRangeBar
					{
						Key = "ringFallback",
						Thickness = 0.05,
						Start = 0d,
						End = UiValue.From(() => face.Value.Level),
					},
				},
			],
		};
	}

	private static UiStack Readout(UiState<TimerFace> face, TimerWidgetSettings settings, UiSize safeArea)
	{
		var children = new List<UiElement>
		{
			new UiWhen
			{
				Key = "stateGate",
				Condition = () => !face.Value.IsRunning && !face.Value.AwaitsDuration,
				Content = () => new UiIcon
				{
					Key = "state",
					Size = 0.1,
					Role = UiComponentTextRoles.Muted,
					Icon = UiValue.From(() => face.Value.IsPaused ? UiIcons.Pause : UiIcons.Play),
				},
			},
			new UiWhen
			{
				Key = "timeGate",
				Condition = () => !face.Value.AwaitsDuration,
				Content = () => Time(face),
			},
			new UiWhen
			{
				Key = "setGate",
				Condition = () => face.Value.AwaitsDuration,
				Content = () => Hint("setHint", AppStrings.Widgets.Countdown.TapToSet(), color: null),
			},
		};

		if (settings is { ShowLabel: true, Label: { } label })
		{
			children.Add(Caption(label, color: null));
		}

		return new UiStack
		{
			Key = "readout",
			Fill = true,
			Padding = safeArea,
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Gap = 0.02,
			Children = children,
		};
	}

	private static UiProgressText Time(UiState<TimerFace> face)
		=> new()
		{
			Key = "time",
			Value = UiValue.From(() => face.Value.Progress),
			Format = face.Value.Kind == TimerWidgetKind.Countdown
				? UiProgressFormats.Remaining
				: UiProgressFormats.Elapsed,
			Size = 0.19,
			MinSize = 0.09,
			Weight = UiComponentTextWeights.Bold,
			Role = UiValue.From(() => face.Value.IsPaused ? UiComponentTextRoles.Muted : UiComponentTextRoles.Primary),
			Align = UiComponentAlignments.Center,
			Fallback = new UiTextRun
			{
				Key = "timeFallback",
				Text = UiText.From(() => face.Value.StaticTime),
				Size = 0.19,
				MinSize = 0.09,
				Weight = UiComponentTextWeights.Bold,
				Align = UiComponentAlignments.Center,
			},
		};

	private static UiButton Finished(TimerWidgetSettings settings, UiSize safeArea)
	{
		var children = new List<UiElement>
		{
			new UiTextRun
			{
				Key = "finishedTime",
				Text = UiText.Of(TimerFace.FormatSeconds(0)),
				Size = 0.19,
				MinSize = 0.09,
				Weight = UiComponentTextWeights.Bold,
				Color = _alertTextColor,
				Align = UiComponentAlignments.Center,
			},
			Hint("dismissHint", AppStrings.Widgets.Countdown.TapToDismiss(), _alertTextColor),
		};

		if (settings is { ShowLabel: true, Label: { } label })
		{
			children.Insert(0, Caption(label, _alertTextColor));
		}

		return new UiButton
		{
			Key = "finished",
			Fill = true,
			Corner = UiComponentButtonCorners.Tile,
			Background = _alertColor,
			BorderStyle = UiComponentBorderStyles.Heartbeat,
			BorderColor = _alertTextColor,
			Padding = safeArea,
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Gap = 0.03,
			Children = children,
		};
	}

	private static UiTextRun Hint(string key, UiText text, string? color)
		=> new()
		{
			Key = key,
			Text = text,
			Size = 0.075,
			MinSize = 0.055,
			Weight = UiComponentTextWeights.Medium,
			Role = UiComponentTextRoles.Secondary,
			Color = color is null ? UiValue.None<string>() : UiValue.Of(color),
			Align = UiComponentAlignments.Center,
			MaxLines = 2,
		};

	private static UiTextRun Caption(string label, string? color)
		=> new()
		{
			Key = "caption",
			Text = UiText.Of(label),
			Size = 0.075,
			MinSize = 0.055,
			Weight = UiComponentTextWeights.SemiBold,
			Role = UiComponentTextRoles.Muted,
			Color = color is null ? UiValue.None<string>() : UiValue.Of(color),
			Align = UiComponentAlignments.Center,
		};

	private static UiValue<string> Tint(string? color)
		=> color is null ? UiValue.None<string>() : UiValue.Of(color);
}
