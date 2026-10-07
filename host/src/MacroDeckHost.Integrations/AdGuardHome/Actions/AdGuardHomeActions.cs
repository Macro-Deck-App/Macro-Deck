using System.Globalization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.AdGuardHome;
using ActionStrings = MacroDeckHost.Localization.AppStrings.Integrations.AdGuardHome.Actions;
using Params = MacroDeckHost.Localization.AppStrings.Integrations.AdGuardHome.Params;
using Durations = MacroDeckHost.Localization.AppStrings.Integrations.AdGuardHome.Durations;

namespace MacroDeckHost.Integrations.AdGuardHome.Actions;

internal static class AdGuardHomeActions
{
	public const string DurationParameter = "duration";

	public const string CustomDurationParameter = "customDuration";

	public const string CustomDuration = "custom";

	private static readonly TimeSpan _maximumPause = TimeSpan.FromDays(7);

	public static IReadOnlyList<IActionDefinition> Create(IAdGuardHomeActionTarget target) =>
	[
		new AdGuardHomeAction("enable-protection", ActionStrings.EnableProtection.Name(), ActionStrings.EnableProtection.Description(),
			target, _ => new AdGuardHomeCommand(AdGuardHomeCommandKind.EnableProtection)),
		new AdGuardHomeAction("disable-protection", ActionStrings.DisableProtection.Name(),
			ActionStrings.DisableProtection.Description(), target,
			_ => new AdGuardHomeCommand(AdGuardHomeCommandKind.DisableProtection)),
		new AdGuardHomeStateAction("toggle-protection", ActionStrings.ToggleProtection.Name(),
			ActionStrings.ToggleProtection.Description(), target,
			_ => new AdGuardHomeCommand(AdGuardHomeCommandKind.ToggleProtection),
			snapshot => snapshot.ProtectionEnabled),
		new AdGuardHomeAction("pause-protection", ActionStrings.PauseProtection.Name(), ActionStrings.PauseProtection.Description(),
			target, PauseCommand,
			[
				ActionParameter.Choice(DurationParameter,
					options:
					[
						.. AdGuardHomeWidgetType.PauseDurations
							.Where(duration => duration.Length is not null)
							.Select(duration => new ActionParameterOption
								{ Value = duration.Id, Label = AdGuardHomeDurationText.Label(duration.Id) }),
						new ActionParameterOption { Value = CustomDuration, Label = Durations.Custom() }
					],
					label: Params.Duration(),
					defaultValue: "5m",
					required: true),
				ActionParameter.Duration(CustomDurationParameter,
					label: Params.CustomDuration(),
					min: 1000,
					max: _maximumPause.TotalMilliseconds,
					defaultMilliseconds: TimeSpan.FromMinutes(10).TotalMilliseconds)
					.OnlyWhen(DurationParameter, CustomDuration)
			]),
		new AdGuardHomeAction("enable-filtering", ActionStrings.EnableFiltering.Name(), ActionStrings.EnableFiltering.Description(),
			target, _ => new AdGuardHomeCommand(AdGuardHomeCommandKind.EnableFiltering)),
		new AdGuardHomeAction("disable-filtering", ActionStrings.DisableFiltering.Name(), ActionStrings.DisableFiltering.Description(),
			target, _ => new AdGuardHomeCommand(AdGuardHomeCommandKind.DisableFiltering)),
		new AdGuardHomeAction("toggle-filtering", ActionStrings.ToggleFiltering.Name(), ActionStrings.ToggleFiltering.Description(),
			target, _ => new AdGuardHomeCommand(AdGuardHomeCommandKind.ToggleFiltering)),
		new AdGuardHomeAction("refresh-filters", ActionStrings.RefreshFilters.Name(), ActionStrings.RefreshFilters.Description(),
			target, _ => new AdGuardHomeCommand(AdGuardHomeCommandKind.RefreshFilters)),
	];

	internal static AdGuardHomeCommand? PauseCommand(IReadOnlyDictionary<string, object> parameters)
	{
		var choice = parameters.GetValueOrDefault(DurationParameter) as string ?? "5m";

		if (choice == CustomDuration)
		{
			return ReadMilliseconds(parameters.GetValueOrDefault(CustomDurationParameter)) is { } milliseconds &&
				milliseconds >= 1000 &&
				milliseconds <= _maximumPause.TotalMilliseconds
					? new AdGuardHomeCommand(AdGuardHomeCommandKind.PauseProtection,
						TimeSpan.FromMilliseconds(milliseconds))
					: null;
		}

		return AdGuardHomeWidgetType.Duration(choice)?.Length is { } length
			? new AdGuardHomeCommand(AdGuardHomeCommandKind.PauseProtection, length)
			: null;
	}

	private static double? ReadMilliseconds(object? value)
		=> value switch
		{
			long number => number,
			int number => number,
			double number => number,
			string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
				=> number,
			_ => null
		};
}
