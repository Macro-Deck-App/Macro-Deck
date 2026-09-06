using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Infrastructure.Triggers;

public sealed class TimeEventProvider : IHostEventProvider
{
	public const string ProviderIdValue = "time";

	public const string IntervalEventId = "interval";
	public const string DailyEventId = "daily";
	public const string WeeklyEventId = "weekly";
	public const string MonthlyEventId = "monthly";
	public const string CronEventId = "cron";

	private static readonly IReadOnlyList<ActionParameterOption> _units =
	[
		new() { Value = "seconds", Label = AppStrings.Events.Time.SecondsLabel() },
		new() { Value = "minutes", Label = AppStrings.Events.Time.MinutesLabel() },
		new() { Value = "hours", Label = AppStrings.Events.Time.HoursLabel() }
	];

	private static readonly IReadOnlyList<ActionParameterOption> _weekdays =
	[
		new() { Value = "MON", Label = AppStrings.Events.Time.MondayLabel() },
		new() { Value = "TUE", Label = AppStrings.Events.Time.TuesdayLabel() },
		new() { Value = "WED", Label = AppStrings.Events.Time.WednesdayLabel() },
		new() { Value = "THU", Label = AppStrings.Events.Time.ThursdayLabel() },
		new() { Value = "FRI", Label = AppStrings.Events.Time.FridayLabel() },
		new() { Value = "SAT", Label = AppStrings.Events.Time.SaturdayLabel() },
		new() { Value = "SUN", Label = AppStrings.Events.Time.SundayLabel() }
	];

	public string ProviderId => ProviderIdValue;

	public LocalizedText ProviderName => AppStrings.Events.Time.ProviderName();

	public IReadOnlyList<EventDefinition> EventDefinitions { get; } =
	[
		new()
		{
			Id = IntervalEventId,
			Name = AppStrings.Events.Time.EveryXName(),
			Description = AppStrings.Events.Time.EveryXDescription(),
			Category = AppStrings.Events.Time.ScheduleCategory(),
			DeliveryKind = EventDeliveryKind.Scheduled,
			ConfigurationParameters =
			[
				ActionParameter.Number("every",
					label: AppStrings.Events.Time.EveryLabel(),
					min: 1,
					max: 100_000,
					step: 1,
					defaultValue: 5),
				ActionParameter.Choice("unit",
					label: AppStrings.Events.Time.UnitLabel(),
					options: _units,
					defaultValue: "minutes")
			],
			PayloadParameters = FiredAt
		},
		new()
		{
			Id = DailyEventId,
			Name = AppStrings.Events.Time.DailyAtName(),
			Category = AppStrings.Events.Time.ScheduleCategory(),
			DeliveryKind = EventDeliveryKind.Scheduled,
			ConfigurationParameters = [TimeOfDay()],
			PayloadParameters = FiredAt
		},
		new()
		{
			Id = WeeklyEventId,
			Name = AppStrings.Events.Time.WeeklyName(),
			Category = AppStrings.Events.Time.ScheduleCategory(),
			DeliveryKind = EventDeliveryKind.Scheduled,
			ConfigurationParameters =
			[
				ActionParameter.Choice("dayOfWeek",
					label: AppStrings.Events.Time.DayLabel(),
					options: _weekdays,
					defaultValue: "MON"),
				TimeOfDay()
			],
			PayloadParameters = FiredAt
		},
		new()
		{
			Id = MonthlyEventId,
			Name = AppStrings.Events.Time.MonthlyName(),
			Description = AppStrings.Events.Time.MonthlyDescription(),
			Category = AppStrings.Events.Time.ScheduleCategory(),
			DeliveryKind = EventDeliveryKind.Scheduled,
			ConfigurationParameters =
			[
				ActionParameter.Number("dayOfMonth",
					label: AppStrings.Events.Time.DayOfMonthLabel(),
					min: 1,
					max: 31,
					step: 1,
					defaultValue: 1),
				TimeOfDay()
			],
			PayloadParameters = FiredAt
		},
		new()
		{
			Id = CronEventId,
			Name = AppStrings.Events.Time.CronExpressionName(),
			Description = AppStrings.Events.Time.CronExpressionDescription(),
			Category = AppStrings.Events.Time.ScheduleCategory(),
			DeliveryKind = EventDeliveryKind.Scheduled,
			ConfigurationParameters =
			[
				ActionParameter.Text("expression",
					label: AppStrings.Events.Time.ExpressionLabel(),
					description: AppStrings.Events.Time.ExpressionDescription(),
					placeholder: "0 9 * * MON-FRI",
					required: true)
			],
			PayloadParameters = FiredAt
		}
	];

	private static IReadOnlyList<ActionParameter> FiredAt =>
	[
		ActionParameter.Text("firedAt", label: AppStrings.Events.Time.FiredAtLabel()),
		ActionParameter.Text("scheduledFor", label: AppStrings.Events.Time.ScheduledForLabel())
	];

	private static ActionParameter TimeOfDay()
		=> ActionParameter.Text("time",
			label: AppStrings.Events.Time.TimeLabel(),
			description: AppStrings.Events.Time.TimeDescription(),
			placeholder: "09:00",
			defaultValue: "09:00",
			required: true);
}
