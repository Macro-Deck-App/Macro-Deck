using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Companion;

internal static class CompanionVariables
{
	private static readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(30);

	private static readonly Slot[] _slots =
	[
		new("is_connected", VariableType.Boolean, null),
		new("battery_level_percent", VariableType.Numeric, 0),
		new("charging", VariableType.Boolean, null),
		new("orientation", VariableType.Text, null),
		new("screen_brightness_percent", VariableType.Numeric, 0),
		new("model", VariableType.Text, null),
		new("platform", VariableType.Text, null),
		new("app_version", VariableType.Text, null)
	];

	public static IReadOnlyList<VariableDefinition> Templates { get; } =
		Declare(VariableNameTemplate.Placeholder("configuration"), null);

	public static IReadOnlyList<VariableDefinition> Declare(string key,
		Guid? entryId,
		LocalizedText configurationName = default)
		=> _slots.Select(slot => VariableDefinition.Eager($"companion_{key}_{slot.Name}",
				slot.Type,
				slot.DecimalPlaces,
				_refreshInterval) with
			{
				DisplayName = DisplayNameFor(slot.Name),
				Id = entryId is { } id
					? $"entry-{id:N}-{slot.Name.Replace('_', '-')}"
					: null,
				Configuration = entryId is { } configId
					? new VariableConfiguration(configId.ToString("N"), configurationName)
					: null
			}).ToList();

	public static bool TryGetConfigurationEntryId(string? definitionId, out Guid entryId)
		=> TryParseDefinitionId(definitionId, out entryId, out _);

	public static bool TrySplit(string definitionId,
		IReadOnlyList<CompanionRuntime> runtimes,
		out CompanionRuntime runtime,
		out string slot)
	{
		runtime = null!;
		if (!TryParseDefinitionId(definitionId, out var entryId, out slot))
		{
			return false;
		}

		var match = runtimes.FirstOrDefault(candidate => candidate.Id == entryId);
		if (match is null)
		{
			slot = string.Empty;
			return false;
		}

		runtime = match;
		return true;
	}

	public static VariableReading Read(CompanionDeviceState? state, string slot)
	{
		if (slot == "is_connected")
		{
			return VariableReading.Of(state is not null);
		}

		return state is not null && Value(state, slot) is { } value
			? VariableReading.Of(value)
			: VariableReading.Unavailable;
	}

	private static object? Value(CompanionDeviceState state, string slot) => slot switch
	{
		"battery_level_percent" => state.BatteryLevelPercent,
		"charging" => state.Charging,
		"orientation" => state.Orientation,
		"screen_brightness_percent" => state.ScreenBrightnessPercent,
		"model" => state.Model,
		"platform" => state.Platform,
		"app_version" => state.AppVersion,
		_ => null
	};

	private static LocalizedText DisplayNameFor(string slot) => slot switch
	{
		"is_connected" => MacroDeckStrings.Connection.Connected(),
		"battery_level_percent" => AppStrings.Integrations.Companion.Variables.BatteryLevelPercent(),
		"charging" => AppStrings.Integrations.Companion.Variables.Charging(),
		"orientation" => AppStrings.Integrations.Companion.Variables.Orientation(),
		"screen_brightness_percent" => AppStrings.Integrations.Companion.Variables.ScreenBrightnessPercent(),
		"model" => AppStrings.Integrations.Companion.Variables.Model(),
		"platform" => AppStrings.Integrations.Companion.Variables.Platform(),
		"app_version" => AppStrings.Integrations.Companion.Variables.AppVersion(),
		_ => default
	};

	private static bool TryParseDefinitionId(string? definitionId, out Guid entryId, out string slot)
	{
		entryId = Guid.Empty;
		slot = string.Empty;
		if (definitionId is null ||
			!definitionId.StartsWith("entry-", StringComparison.Ordinal) ||
			definitionId.Length <= 39 ||
			definitionId[38] != '-' ||
			!Guid.TryParseExact(definitionId.AsSpan(6, 32), "N", out entryId))
		{
			return false;
		}

		var candidate = definitionId[39..].Replace('-', '_');
		if (!_slots.Any(s => string.Equals(s.Name, candidate, StringComparison.Ordinal)))
		{
			return false;
		}

		slot = candidate;
		return true;
	}

	private sealed record Slot(string Name, VariableType Type, int? DecimalPlaces);
}
