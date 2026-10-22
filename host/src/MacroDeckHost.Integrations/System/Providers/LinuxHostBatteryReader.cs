using System.Globalization;
using MacroDeck.Sdk.Batteries;

namespace MacroDeckHost.Integrations.System.Providers;

internal sealed class LinuxHostBatteryReader : IHostBatteryReader
{
	public const string DefaultRoot = "/sys/class/power_supply";

	private readonly string _root;

	public LinuxHostBatteryReader(string root = DefaultRoot)
	{
		_root = root;
	}

	public Task<IReadOnlyList<HostBatteryReading>?> ReadAsync(CancellationToken cancellationToken)
	{
		return Task.FromResult<IReadOnlyList<HostBatteryReading>?>(Read(_root));
	}

	private static List<HostBatteryReading>? Read(string root)
	{
		try
		{
			if (!Directory.Exists(root))
			{
				return [];
			}

			return Directory.EnumerateFileSystemEntries(root)
				.Where(Directory.Exists)
				.Order(StringComparer.Ordinal)
				.Where(IsSystemBattery)
				.Select(ReadingOf)
				.ToList();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	// Peripherals such as mice and headsets report scope Device; only the computer's own batteries count.
	private static bool IsSystemBattery(string supply)
	{
		var scope = Attribute(supply, "scope");
		return string.Equals(Attribute(supply, "type"), "Battery", StringComparison.OrdinalIgnoreCase)
			&& (scope is null || string.Equals(scope, "System", StringComparison.OrdinalIgnoreCase))
			&& Attribute(supply, "present") != "0";
	}

	private static HostBatteryReading ReadingOf(string supply)
	{
		var level = Number(supply, "capacity");
		var chargeState = Attribute(supply, "status")?.ToLowerInvariant() switch
		{
			"charging" => BatteryChargeState.Charging,
			"discharging" => BatteryChargeState.Discharging,
			"full" => BatteryChargeState.Full,
			"not charging" => BatteryChargeState.NotCharging,
			_ => BatteryChargeState.Unknown
		};

		return new HostBatteryReading(Path.GetFileName(supply),
			level is >= 0 and <= 100 ? (int)level : null,
			chargeState,
			chargeState == BatteryChargeState.Discharging ? Seconds(supply, "time_to_empty_now") : null,
			chargeState == BatteryChargeState.Charging ? Seconds(supply, "time_to_full_now") : null);
	}

	private static TimeSpan? Seconds(string supply, string name)
	{
		return Number(supply, name) is long seconds and > 0 ? TimeSpan.FromSeconds(seconds) : null;
	}

	private static long? Number(string supply, string name)
	{
		return long.TryParse(Attribute(supply, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
			? value
			: null;
	}

	// A driver may refuse to read a single attribute while the battery settles; that value is then unknown.
	private static string? Attribute(string supply, string name)
	{
		try
		{
			var path = Path.Combine(supply, name);
			return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}
}
