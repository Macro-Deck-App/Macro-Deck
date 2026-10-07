using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.AdGuardHome;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.AdGuardHome.Variables;

namespace MacroDeckHost.Integrations.AdGuardHome;

internal static class AdGuardHomeVariables
{
	public const string Prefix = "adguard_home_";

	public const string IsReachable = "is_reachable";

	private static readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(5);

	private static readonly IReadOnlyList<Variable> _variables =
	[
		new(IsReachable, VariableType.Boolean, Strings.IsReachable),
		new("protection_enabled", VariableType.Boolean, Strings.ProtectionEnabled),
		new("protection_disabled_until", VariableType.Text, Strings.ProtectionDisabledUntil),
		new("dns_queries", VariableType.Numeric, Strings.DnsQueries, DecimalPlaces: 0),
		new("blocked_queries", VariableType.Numeric, Strings.BlockedQueries, DecimalPlaces: 0),
		new("blocked_percentage", VariableType.Numeric, Strings.BlockedPercentage, DecimalPlaces: 2, Unit: "%",
			SemanticKind: VariableSemanticKinds.Percentage),
		new("avg_processing_time", VariableType.Numeric, Strings.AverageProcessingTime, DecimalPlaces: 2, Unit: "ms"),
		new("safebrowsing_blocked", VariableType.Numeric, Strings.SafeBrowsingBlocked, DecimalPlaces: 0),
		new("parental_blocked", VariableType.Numeric, Strings.ParentalBlocked, DecimalPlaces: 0),
		new("safesearch_enforced", VariableType.Numeric, Strings.SafeSearchEnforced, DecimalPlaces: 0),
		new("version", VariableType.Text, Strings.Version),
	];

	public static IReadOnlyList<string> Names { get; } = [.. _variables.Select(variable => variable.Name)];

	public static IReadOnlyList<VariableDefinition> Declare(string variableKey, VariableConfiguration? configuration = null)
		=>
		[
			.. _variables.Select(variable => VariableDefinition.Eager($"{Prefix}{variableKey}_{variable.Name}",
					variable.Type,
					variable.DecimalPlaces,
					_refreshInterval)
				with
				{
					DisplayName = variable.Label(),
					Configuration = configuration,
					Unit = variable.Unit,
					SemanticKind = variable.SemanticKind
				})
		];

	public static (string VariableKey, string Name)? SplitDefinitionId(string definitionId)
	{
		var variableName = definitionId.Replace('-', '_');
		if (!variableName.StartsWith(Prefix, StringComparison.Ordinal))
		{
			return null;
		}

		var remainder = variableName[Prefix.Length..];
		foreach (var name in Names)
		{
			var suffix = $"_{name}";
			if (remainder.Length > suffix.Length && remainder.EndsWith(suffix, StringComparison.Ordinal))
			{
				return (remainder[..^suffix.Length], name);
			}
		}

		return null;
	}

	public static VariableReading Read(AdGuardHomeSnapshot? snapshot, string name)
	{
		if (snapshot is null)
		{
			return VariableReading.Unavailable;
		}

		if (name == IsReachable)
		{
			return VariableReading.Of(snapshot.IsConnected);
		}

		if (!snapshot.IsConnected)
		{
			return VariableReading.Unavailable;
		}

		var stats = snapshot.Statistics;
		object? value = name switch
		{
			"protection_enabled" => snapshot.ProtectionEnabled,
			"protection_disabled_until" => snapshot.DisabledUntil?.ToString("o", CultureInfo.InvariantCulture) ??
				string.Empty,
			"dns_queries" => stats?.DnsQueries,
			"blocked_queries" => stats?.BlockedFiltering,
			"blocked_percentage" => stats is null ? null : Math.Round(stats.BlockedPercentage, 2),
			"avg_processing_time" => stats is null ? null : Math.Round(stats.AverageProcessingMilliseconds, 2),
			"safebrowsing_blocked" => stats?.SafeBrowsing,
			"parental_blocked" => stats?.Parental,
			"safesearch_enforced" => stats?.SafeSearch,
			"version" => snapshot.Version,
			_ => null
		};

		return value is null ? VariableReading.Unavailable : VariableReading.Of(value);
	}

	private sealed record Variable(
		string Name,
		VariableType Type,
		Func<LocalizedString> Label,
		int? DecimalPlaces = null,
		string? Unit = null,
		string? SemanticKind = null);
}
