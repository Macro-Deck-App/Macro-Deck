using MacroDeck.Localization;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// The <c>host.state</c> data for <see cref="HostApis.MusicPlayers" />, the same for every plugin: the players of
/// all providers. Sent on registration and whenever the list changes. A push whose revision is at or below the
/// last one applied in the same session is stale and ignored. An SDK that predates it ignores the push.
/// </summary>
public sealed record MusicPlayersStateDto
{
	public long Revision { get; init; }

	public IReadOnlyList<MusicPlayerInstanceStateDto> Instances { get; init; } = [];
}

/// <summary>One player in a <see cref="MusicPlayersStateDto" />.</summary>
public sealed record MusicPlayerInstanceStateDto
{
	/// <summary><c>&lt;provider id&gt;::&lt;instance id&gt;</c>.</summary>
	public required string Id { get; init; }

	public required string ProviderId { get; init; }

	public LocalizedText ProviderName { get; init; }

	public required string DisplayName { get; init; }
}

/// <summary>The arguments of <c>music-players</c>/<c>album-color</c>.</summary>
public sealed record MusicPlayerAlbumColorArguments
{
	/// <summary>A <see cref="MusicPlayerInstanceStateDto.Id" />, at most 512 characters.</summary>
	public required string InstanceId { get; init; }
}

/// <summary>The result of <c>music-players</c>/<c>album-color</c>.</summary>
public sealed record MusicPlayerAlbumColorResult
{
	/// <summary>Lowercase <c>#rrggbb</c>, or absent when the host knows no colour for the player.</summary>
	public string? Color { get; init; }
}

/// <summary>Converts the SDK's music player snapshot to and from its <c>music-players</c> wire shape.</summary>
public static class MusicPlayerWire
{
	public static MusicPlayerInstanceStateDto ToDto(MusicPlayerInstanceSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		return new MusicPlayerInstanceStateDto
		{
			Id = snapshot.Id,
			ProviderId = snapshot.ProviderId,
			ProviderName = snapshot.ProviderName,
			DisplayName = snapshot.DisplayName
		};
	}

	public static MusicPlayerInstanceSnapshot ToSnapshot(MusicPlayerInstanceStateDto dto)
	{
		ArgumentNullException.ThrowIfNull(dto);

		return new MusicPlayerInstanceSnapshot
		{
			Id = dto.Id,
			ProviderId = dto.ProviderId ?? string.Empty,
			ProviderName = dto.ProviderName,
			DisplayName = dto.DisplayName ?? string.Empty
		};
	}
}
