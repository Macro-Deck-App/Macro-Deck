using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.References;
using MacroDeck.Ui.Components;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Widgets.Clock;

internal static class ClockWidgetView
{
	// A format the reader's own language does not choose is negotiated rather than merely sent: an
	// unknown format value is not caught anywhere, so a reader that predates one draws an empty run.
	// Asking for version 2 turns that blank clock into the version 1 run carried as the node's fallback.
	private const int _pinnedFormatVersion = 2;

	public static UiElement Build(ClockWidgetData config,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius)
	{
		ArgumentNullException.ThrowIfNull(config);

		var reference = UiValue.Of(UiTimeReference.InZone(config.TimeZone));
		var children = new List<UiElement>();

		if (config.ShowLabel && (config.Label is not null || config.TimeZone is not null))
		{
			children.Add(Caption(config, reference));
		}

		children.AddRange(Face(config, reference));

		if (config.ShowOffset && config.TimeZone is not null)
		{
			children.Add(Offset(config, reference));
		}

		return new UiStack
		{
			Key = "clock",
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Gap = 0.03,
			Padding = WidgetSafeArea.For(cornerRadius),
			Background = Tint(config.BackgroundColor),
			Children = children,
		};
	}

	/// <summary>The time and the date in the order and on the axis the date's position asks for.</summary>
	private static IReadOnlyList<UiElement> Face(ClockWidgetData config, UiValue<UiTimeReference> reference)
	{
		UiElement time = config.IsAnalog ? Dial(config, reference) : DigitalTime(config, reference);

		if (!config.ShowDate)
		{
			return [time];
		}

		var date = Date(config, reference);

		return config.DatePosition switch
		{
			ClockDatePosition.Above => [date, time],
			ClockDatePosition.Left => [Row(config, "dateRow", [date, time])],
			ClockDatePosition.Right => [Row(config, "dateRow", [time, date])],
			_ => [time, date],
		};
	}

	private static UiStack Row(ClockWidgetData config, string key, IReadOnlyList<UiElement> children)
		=> new()
		{
			Key = key,
			Direction = UiComponentDirections.Horizontal,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Gap = 0.04,
			Children = children,
		};

	private static UiElement Caption(ClockWidgetData config, UiValue<UiTimeReference> reference)
	{
		if (config.Label is { } label)
		{
			return new UiTextRun
			{
				Key = "caption",
				Text = UiText.Of(label),
				Size = 0.08,
				Weight = UiComponentTextWeights.SemiBold,
				Role = UiComponentTextRoles.Secondary,
				Color = Tint(config.TextColor),
				Align = UiComponentAlignments.Center,
			};
		}

		return new UiDynamicText
		{
			Key = "caption",
			Value = reference,
			Format = UiTimeFormats.ZoneName,
			Size = 0.08,
			Weight = UiComponentTextWeights.SemiBold,
			Role = UiComponentTextRoles.Secondary,
			Color = Tint(config.TextColor),
			Align = UiComponentAlignments.Center,
			Fallback = Omitted("captionFallback"),
		};
	}

	private static UiDynamicText DigitalTime(ClockWidgetData config, UiValue<UiTimeReference> reference)
	{
		var pinned = TimeFormat(config);

		return pinned is null
			? TimeRun(config, reference, "time", UiTimeFormats.Time, UpdateRequired("timeFallback"))
			: TimeRun(config,
					reference,
					"time",
					pinned,
					TimeRun(config,
						reference,
						"timeLocalized",
						UiTimeFormats.Time,
						UpdateRequired("timeFallback"))) with
				{
					RequiredComponentVersion = _pinnedFormatVersion,
				};
	}

	private static UiDynamicText TimeRun(ClockWidgetData config,
		UiValue<UiTimeReference> reference,
		string key,
		string format,
		UiElement fallback)
		=> new()
		{
			Key = key,
			Value = reference,
			Format = format,
			Seconds = config.ShowSeconds,
			Size = 0.24,
			MinSize = 0.13,
			Weight = UiComponentTextWeights.Bold,
			Role = UiComponentTextRoles.Primary,
			Color = Tint(config.TextColor),
			Align = UiComponentAlignments.Center,
			Fallback = fallback,
		};

	private static UiClockDial Dial(ClockWidgetData config, UiValue<UiTimeReference> reference)
		=> new()
		{
			Key = "dial",
			Value = reference,
			Seconds = config.ShowSeconds,
			Color = Tint(config.TextColor),
			Fill = true,

			// A reader that draws no dial but knows a dynamic text still shows the right time, which beats
			// both a placeholder and a second face nobody configured. It is spelled with the reader's own
			// format rather than the configured one: this branch is already the degraded path, and pinning
			// a format here would only add a third step to the chain for the readers least likely to
			// resolve it.
			Fallback = TimeRun(config,
				reference,
				"dialFallback",
				UiTimeFormats.Time,
				UpdateRequired("dialNoticeFallback")),
		};

	private static UiDynamicText Date(ClockWidgetData config, UiValue<UiTimeReference> reference)
	{
		var pinned = DateFormat(config);

		return pinned is null
			? DateRun(config, reference, "date", UiTimeFormats.Date, Omitted("dateFallback"))
			: DateRun(config,
					reference,
					"date",
					pinned,
					DateRun(config,
						reference,
						"dateLocalized",
						UiTimeFormats.Date,
						Omitted("dateFallback"))) with
				{
					RequiredComponentVersion = _pinnedFormatVersion,
				};
	}

	private static UiDynamicText DateRun(ClockWidgetData config,
		UiValue<UiTimeReference> reference,
		string key,
		string format,
		UiElement fallback)
		=> new()
		{
			Key = key,
			Value = reference,
			Format = format,
			Size = 0.08,
			Weight = UiComponentTextWeights.Medium,
			Role = UiComponentTextRoles.Muted,
			Color = Tint(config.TextColor),
			Align = UiComponentAlignments.Center,
			Fallback = fallback,
		};

	private static UiDynamicText Offset(ClockWidgetData config, UiValue<UiTimeReference> reference)
		=> new()
		{
			Key = "offset",
			Value = reference,
			Format = UiTimeFormats.ZoneOffset,
			Size = 0.07,
			Weight = UiComponentTextWeights.Medium,
			Role = UiComponentTextRoles.Muted,
			Color = Tint(config.TextColor),
			Align = UiComponentAlignments.Center,
			RequiredComponentVersion = _pinnedFormatVersion,

			// Supporting detail rather than a second time: a reader with no offset format has nothing to
			// put here, and an empty run beside a clock that is already right says everything it needs to.
			Fallback = Omitted("offsetFallback"),
		};

	/// <summary>The format value the configured face and padding name, or null for the reader's own.</summary>
	private static string? TimeFormat(ClockWidgetData config)
		=> config.HourCycle switch
		{
			ClockHourCycle.TwelveHour => config.LeadingZero
				? UiTimeFormats.Time12HourPadded
				: UiTimeFormats.Time12Hour,
			ClockHourCycle.TwentyFourHour => config.LeadingZero
				? UiTimeFormats.Time24Hour
				: UiTimeFormats.Time24HourUnpadded,
			_ => null,
		};

	/// <summary>The format value the configured date names, or null for the reader's own short date.</summary>
	private static string? DateFormat(ClockWidgetData config)
		=> config.DateFormat switch
		{
			ClockDateFormat.DayFirst => UiTimeFormats.DateDayFirst,
			ClockDateFormat.MonthFirst => UiTimeFormats.DateMonthFirst,
			ClockDateFormat.Iso => UiTimeFormats.DateIso,
			ClockDateFormat.Written => UiTimeFormats.DateLong,
			_ => null,
		};

	private static UiValue<string> Tint(string? color)
		=> color is null ? UiValue.None<string>() : UiValue.Of(color);

	private static UiTextRun UpdateRequired(string key)
		=> new()
		{
			Key = key,
			Text = UiText.FromLocalized(AppStrings.Widgets.Clock.UpdateRequired),
			Size = 0.08,
			MinSize = 0.06,
			Weight = UiComponentTextWeights.Medium,
			Role = UiComponentTextRoles.Muted,
			Align = UiComponentAlignments.Center,
			MaxLines = 3,
		};

	// Supporting detail an older reader simply does without, rather than a placeholder beside a notice it
	// already shows once.
	private static UiTextRun Omitted(string key)
		=> new() { Key = key, Text = UiText.Of(string.Empty) };
}
