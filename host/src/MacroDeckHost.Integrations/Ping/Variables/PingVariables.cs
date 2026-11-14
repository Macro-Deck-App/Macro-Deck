using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Ping.State;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Ping.Variables;

namespace MacroDeckHost.Integrations.Ping.Variables;

internal static class PingVariables
{
	public const string Online = "online";

	public const string Latency = "latency";

	public const string PacketLoss = "packet_loss";

	public const string LastSeen = "last_seen";

	private const string EntryPrefix = "entry-";

	private static readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(2);

	private static readonly Slot[] _slots =
	[
		new(Online, VariableType.Boolean, Strings.Online),
		new(Latency, VariableType.Numeric, Strings.Latency, 0, "ms"),
		new(PacketLoss, VariableType.Numeric, Strings.PacketLoss, 1, "%", VariableSemanticKinds.Percentage),
		new(LastSeen, VariableType.Text, Strings.LastSeen)
	];

	public static IReadOnlyList<VariableDefinition> Templates { get; } =
		Declare(VariableNameTemplate.Placeholder("target"), null);

	public static IReadOnlyList<VariableDefinition> Declare(
		string key,
		Guid? entryId,
		LocalizedText targetName = default)
	{
		return
		[
			.. _slots.Select(slot => VariableDefinition.Eager(Name(key, slot.Name),
					slot.Type,
					slot.DecimalPlaces,
					_refreshInterval)
				with
				{
					DisplayName = slot.Label(),
					Unit = slot.Unit,
					SemanticKind = slot.SemanticKind,
					Id = entryId is { } id ? $"{EntryPrefix}{id:N}-{slot.Name.Replace('_', '-')}" : null,
					Configuration = entryId is { } configId
						? new VariableConfiguration(configId.ToString("N"), targetName)
						: null
				})
		];
	}

	public static string Name(string key, string slot)
	{
		return $"ping_{key}_{slot}";
	}

	public static bool TrySplit(string? definitionId, out Guid entryId, out string slot)
	{
		entryId = Guid.Empty;
		slot = string.Empty;
		if (definitionId is null
			|| !definitionId.StartsWith(EntryPrefix, StringComparison.Ordinal)
			|| definitionId.Length <= EntryPrefix.Length + 33
			|| definitionId[EntryPrefix.Length + 32] != '-'
			|| !Guid.TryParseExact(definitionId.AsSpan(EntryPrefix.Length, 32), "N", out entryId))
		{
			return false;
		}

		var candidate = definitionId[(EntryPrefix.Length + 33)..].Replace('-', '_');
		if (!_slots.Any(known => string.Equals(known.Name, candidate, StringComparison.Ordinal)))
		{
			return false;
		}

		slot = candidate;
		return true;
	}

	public static VariableReading Read(PingSnapshot? snapshot, string slot)
	{
		if (snapshot is null)
		{
			return VariableReading.Unavailable;
		}

		object? value = slot switch
		{
			Online => snapshot.Reachability == PingReachability.Unknown ? null : snapshot.IsOnline,
			Latency => snapshot.IsOnline ? snapshot.LatencyMilliseconds : null,
			PacketLoss => snapshot.PacketLossPercent,
			LastSeen => snapshot.LastSeen?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
			_ => null
		};

		return value is null ? VariableReading.Unavailable : VariableReading.Of(value);
	}

	private sealed record Slot(
		string Name,
		VariableType Type,
		Func<LocalizedString> Label,
		int? DecimalPlaces = null,
		string? Unit = null,
		string? SemanticKind = null);
}
