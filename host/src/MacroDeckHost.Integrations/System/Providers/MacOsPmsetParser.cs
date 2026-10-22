using System.Globalization;
using System.Text.RegularExpressions;
using MacroDeck.Sdk.Batteries;

namespace MacroDeckHost.Integrations.System.Providers;

internal static partial class MacOsPmsetParser
{
	public static IReadOnlyList<HostBatteryReading>? Parse(string output)
	{
		if (string.IsNullOrWhiteSpace(output))
		{
			return null;
		}

		var readings = new List<HostBatteryReading>();
		foreach (var line in output.Split('\n'))
		{
			if (BatteryLine().Match(line) is not { Success: true } match
				|| !match.Groups["present"].Value.Equals("true", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var fields = match.Groups["fields"].Value.Split(';', StringSplitOptions.TrimEntries);
			var chargeState = ChargeStateOf(fields[0]);
			var estimate = EstimateOf(fields);
			readings.Add(new HostBatteryReading(match.Groups["index"].Value,
				LevelOf(match.Groups["level"].Value),
				chargeState,
				chargeState == BatteryChargeState.Discharging ? estimate : null,
				chargeState == BatteryChargeState.Charging ? estimate : null));
		}

		return readings;
	}

	private static int? LevelOf(string text)
	{
		return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var level) && level <= 100
			? level
			: null;
	}

	private static BatteryChargeState ChargeStateOf(string state)
	{
		return state.ToLowerInvariant() switch
		{
			"charging" or "finishing charge" => BatteryChargeState.Charging,
			"discharging" => BatteryChargeState.Discharging,
			"charged" => BatteryChargeState.Full,
			"ac attached" => BatteryChargeState.NotCharging,
			_ => BatteryChargeState.Unknown
		};
	}

	// pmset prints 0:00 while it is still computing an estimate, so a zero estimate counts as none.
	private static TimeSpan? EstimateOf(IEnumerable<string> fields)
	{
		foreach (var field in fields)
		{
			if (Remaining().Match(field) is { Success: true } match)
			{
				var estimate = new TimeSpan(int.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture),
					int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture),
					0);
				return estimate > TimeSpan.Zero ? estimate : null;
			}
		}

		return null;
	}

	[GeneratedRegex(@"-InternalBattery-(?<index>\d+).*?\t\s*(?<level>\d+)%;(?<fields>.*)present:\s*(?<present>\w+)")]
	private static partial Regex BatteryLine();

	[GeneratedRegex(@"^(?<hours>\d+):(?<minutes>\d{2}) remaining$")]
	private static partial Regex Remaining();
}
