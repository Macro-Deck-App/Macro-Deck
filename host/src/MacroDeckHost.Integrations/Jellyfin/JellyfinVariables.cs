using MacroDeck.Localization;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Jellyfin.Variables;

namespace MacroDeckHost.Integrations.Jellyfin;

internal static class JellyfinVariables
{
	public const string TotalActiveSessionsName = "jellyfin_active_sessions";

	// Declared ids and variable names are both capped at 64 characters; these bounds keep the longest
	// server and device keys plus the longest slot inside that.
	public const int MaxServerKeyLength = 17;

	public const int MaxDeviceKeyLength = 19;

	private const string TotalActiveSessionsId = "active-sessions";
	private const string ServerPrefix = "entry-";
	private const string DevicePrefix = "dev-";

	private static readonly TimeSpan _refresh = TimeSpan.FromSeconds(1);

	private static readonly Slot[] _serverSlots =
	[
		new("is_connected", VariableType.Boolean, null, null),
		new("active_sessions", VariableType.Numeric, 0, null)
	];

	private static readonly VariableWriteCapability _toggle = new();

	private static readonly Slot[] _deviceSlots =
	[
		new("is_active", VariableType.Boolean, null, null),
		new("is_playing", VariableType.Boolean, null, null, _toggle),
		new("is_paused", VariableType.Boolean, null, null, _toggle),
		new("title", VariableType.Text, null, null),
		new("series", VariableType.Text, null, null),
		new("album", VariableType.Text, null, null),
		new("media_type", VariableType.Text, null, null),
		new("progress_percent", VariableType.Numeric, 0, "%", MusicPlayerVariableWrites.Position),
		new("position", VariableType.Numeric, 0, "s", MusicPlayerVariableWrites.Position),
		new("duration", VariableType.Numeric, 0, "s"),
		new("user", VariableType.Text, null, null),
		new("client", VariableType.Text, null, null),
		new("device", VariableType.Text, null, null),
		new("is_transcoding", VariableType.Boolean, null, null),
		new("volume", VariableType.Numeric, 0, "%", MusicPlayerVariableWrites.Volume),
		new("is_muted", VariableType.Boolean, null, null, _toggle)
	];

	public static IReadOnlyList<string> DeviceSlotNames { get; } = [.. _deviceSlots.Select(slot => slot.Name)];

	public static bool IsWritable(string slot) => _deviceSlots.Any(candidate => candidate.Name == slot && candidate.Write is not null);

	public static IReadOnlyList<string> ServerSlotNames { get; } = [.. _serverSlots.Select(slot => slot.Name)];

	public static IReadOnlyList<VariableDefinition> Templates { get; } =
	[
		Total(),
		.. DeclareServer(VariableNameTemplate.Placeholder("server"), null, default),
		.. DeclareDevice(VariableNameTemplate.Placeholder("server"),
			VariableNameTemplate.Placeholder("device"),
			null,
			null,
			default)
	];

	public static VariableDefinition Total()
		=> VariableDefinition.Eager(TotalActiveSessionsName, VariableType.Numeric, 0, _refresh) with
		{
			Id = TotalActiveSessionsId,
			DisplayName = Strings.TotalActiveSessions()
		};

	public static IReadOnlyList<VariableDefinition> DeclareServer(string serverKey, Guid? entryId,
		LocalizedText title)
		=> _serverSlots.Select(slot => Define($"jellyfin_{serverKey}_{slot.Name}",
				slot,
				entryId is { } id ? $"{ServerPrefix}{id:N}-{Dashed(slot.Name)}" : null,
				ServerDisplayName(slot.Name),
				entryId,
				title))
			.ToList();

	public static IReadOnlyList<VariableDefinition> DeclareDevice(
		string serverKey,
		string deviceKey,
		Guid? entryId,
		string? deviceLocalId,
		LocalizedText title)
		=> _deviceSlots.Select(slot => Define(DeviceVariableName(serverKey, deviceKey, slot.Name),
				slot,
				entryId is { } id && deviceLocalId is not null
					? $"{DevicePrefix}{id:N}-{deviceLocalId}-{Dashed(slot.Name)}"
					: null,
				DeviceDisplayName(slot.Name),
				entryId,
				title))
			.ToList();

	public static string DeviceVariableName(string serverKey, string deviceKey, string slot)
		=> $"jellyfin_{serverKey}_{deviceKey}_{slot}";

	public static string ServerVariableName(string serverKey, string slot) => $"jellyfin_{serverKey}_{slot}";

	public static bool TryGetEntryId(string? definitionId, out Guid entryId)
	{
		entryId = Guid.Empty;
		if (definitionId is null)
		{
			return false;
		}

		var prefix = definitionId.StartsWith(ServerPrefix, StringComparison.Ordinal) ? ServerPrefix :
			definitionId.StartsWith(DevicePrefix, StringComparison.Ordinal) ? DevicePrefix : null;
		return prefix is not null &&
			definitionId.Length > prefix.Length + 32 &&
			Guid.TryParseExact(definitionId.AsSpan(prefix.Length, 32), "N", out entryId);
	}

	public static VariableTarget Parse(string localId)
	{
		if (string.Equals(localId, TotalActiveSessionsId, StringComparison.Ordinal))
		{
			return new VariableTarget(VariableTargetKind.Total, Guid.Empty, null, string.Empty);
		}

		if (!TryGetEntryId(localId, out var entryId))
		{
			return VariableTarget.None;
		}

		if (localId.StartsWith(ServerPrefix, StringComparison.Ordinal))
		{
			var slot = Undashed(localId[(ServerPrefix.Length + 33)..]);
			return ServerSlotNames.Contains(slot)
				? new VariableTarget(VariableTargetKind.Server, entryId, null, slot)
				: VariableTarget.None;
		}

		var rest = localId[(DevicePrefix.Length + 33)..];
		var separator = rest.IndexOf('-', StringComparison.Ordinal);
		if (separator <= 0)
		{
			return VariableTarget.None;
		}

		var deviceSlot = Undashed(rest[(separator + 1)..]);
		return DeviceSlotNames.Contains(deviceSlot)
			? new VariableTarget(VariableTargetKind.Device, entryId, rest[..separator], deviceSlot)
			: VariableTarget.None;
	}

	public static object? ReadServer(JellyfinServerState state, string slot) => slot switch
	{
		"is_connected" => state.IsConnected,
		"active_sessions" => state.IsConnected ? state.ActiveSessions.Count : 0,
		_ => null
	};

	// Session end writes typed empties rather than null: a null report leaves the previous value in place.
	public static object? ReadDevice(JellyfinSession? session, JellyfinKnownDevice device, string slot,
		DateTimeOffset now)
	{
		var item = session?.NowPlaying;
		return slot switch
		{
			"is_active" => item is not null,
			"is_playing" => session?.IsPlaying ?? false,
			"is_paused" => item is not null && session!.IsPaused,
			"title" => item?.Name ?? string.Empty,
			"series" => item?.SeriesName ?? string.Empty,
			"album" => item?.IsAudio == true ? item.Album ?? string.Empty : item?.SeasonName ?? string.Empty,
			"media_type" => item?.Type ?? string.Empty,
			"progress_percent" => item is null ? 0 : Math.Round(session!.ProgressPercentAt(now) ?? 0),
			"position" => item is null ? 0 : Math.Round(session!.PositionAt(now)?.TotalSeconds ?? 0),
			"duration" => Math.Round(item?.Duration?.TotalSeconds ?? 0),
			"user" => item is null ? string.Empty : session!.UserName ?? string.Empty,
			"client" => session?.Client ?? device.Client,
			"device" => session?.DeviceName ?? device.Name,
			"is_transcoding" => item is not null && session!.IsTranscoding,
			"volume" => session?.VolumePercent ?? 0,
			"is_muted" => session?.IsMuted ?? false,
			_ => null
		};
	}

	private static VariableDefinition Define(string name, Slot slot, string? id, LocalizedText displayName,
		Guid? entryId, LocalizedText title)
		=> VariableDefinition.Eager(name, slot.Type, slot.DecimalPlaces, _refresh) with
		{
			Id = id,
			DisplayName = displayName,
			Unit = slot.Unit,
			Write = slot.Write,
			Configuration = entryId is { } configId ? new VariableConfiguration(configId.ToString("N"), title) : null
		};

	private static LocalizedText ServerDisplayName(string slot) => slot switch
	{
		"is_connected" => MacroDeckStrings.Connection.Connected(),
		"active_sessions" => Strings.ActiveSessions(),
		_ => default
	};

	private static LocalizedText DeviceDisplayName(string slot) => slot switch
	{
		"is_active" => Strings.IsActive(),
		"is_playing" => Strings.IsPlaying(),
		"is_paused" => Strings.IsPaused(),
		"title" => Strings.Title(),
		"series" => Strings.Series(),
		"album" => Strings.Album(),
		"media_type" => Strings.MediaType(),
		"progress_percent" => Strings.ProgressPercent(),
		"position" => Strings.Position(),
		"duration" => Strings.Duration(),
		"user" => Strings.User(),
		"client" => Strings.Client(),
		"device" => Strings.Device(),
		"is_transcoding" => Strings.IsTranscoding(),
		"volume" => Strings.Volume(),
		"is_muted" => Strings.IsMuted(),
		_ => default
	};

	private static string Dashed(string slot) => slot.Replace('_', '-');

	private static string Undashed(string slot) => slot.Replace('-', '_');

	private sealed record Slot(
		string Name,
		VariableType Type,
		int? DecimalPlaces,
		string? Unit,
		VariableWriteCapability? Write = null);
}

internal enum VariableTargetKind
{
	None,
	Total,
	Server,
	Device
}

internal sealed record VariableTarget(VariableTargetKind Kind, Guid EntryId, string? DeviceLocalId, string Slot)
{
	public static VariableTarget None { get; } = new(VariableTargetKind.None, Guid.Empty, null, string.Empty);
}
