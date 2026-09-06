using System.Text.Json;

namespace MacroDeckHost.Integrations.YtmDesktop.Protocol;

internal static class YtmDesktopStateReader
{
	public static YtmPlayerState Read(JsonElement root)
	{
		var player = ReadPlayer(root);

		var video = root.ValueKind == JsonValueKind.Object &&
			root.TryGetProperty("video", out var videoElement) &&
			videoElement.ValueKind == JsonValueKind.Object
				? ReadVideo(videoElement)
				: null;

		return new YtmPlayerState(player, video);
	}

	private static YtmPlayerInfo ReadPlayer(JsonElement root)
	{
		if (root.ValueKind != JsonValueKind.Object ||
			!root.TryGetProperty("player", out var player) ||
			player.ValueKind != JsonValueKind.Object)
		{
			return new YtmPlayerInfo(YtmTrackState.Unknown, 0, 0, null, false, null);
		}

		var queue = player.TryGetProperty("queue", out var queueElement) &&
			queueElement.ValueKind == JsonValueKind.Object
				? ReadQueue(queueElement)
				: null;

		return new YtmPlayerInfo(ReadEnum(player, "trackState", YtmTrackState.Unknown),
			ReadDouble(player, "videoProgress") ?? 0,
			ReadInt(player, "volume") ?? 0,
			ReadBool(player, "muted"),
			ReadBool(player, "adPlaying") ?? false,
			queue);
	}

	private static YtmQueueInfo ReadQueue(JsonElement queue)
		=> new(ReadEnum(queue, "repeatMode", YtmRepeatMode.Unknown),
			ReadInt(queue, "selectedItemIndex") ?? 0,
			ReadQueueItems(queue));

	private static List<YtmQueueItem> ReadQueueItems(JsonElement queue)
	{
		if (queue.ValueKind != JsonValueKind.Object ||
			!queue.TryGetProperty("items", out var items) ||
			items.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var result = new List<YtmQueueItem>(items.GetArrayLength());
		foreach (var item in items.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			if (ReadString(item, "videoId") is not { } videoId)
			{
				continue;
			}

			result.Add(new YtmQueueItem(videoId,
				ReadString(item, "title") ?? string.Empty,
				ReadString(item, "author"),
				ReadString(item, "duration"),
				ReadBool(item, "selected") ?? false,
				ReadThumbnails(item)));
		}

		return result;
	}

	private static YtmVideoInfo ReadVideo(JsonElement video)
		=> new(ReadString(video, "id") ?? string.Empty,
			ReadString(video, "title") ?? string.Empty,
			ReadString(video, "author"),
			ReadString(video, "channelId"),
			ReadString(video, "album"),
			ReadString(video, "albumId"),
			ReadNullableEnum(video, "likeStatus", YtmLikeStatus.Unknown),
			ReadThumbnails(video),
			ReadInt(video, "durationSeconds") ?? 0,
			ReadBool(video, "isLive"),
			ReadNullableEnum(video, "videoType", YtmVideoType.Unknown),
			ReadBool(video, "metadataFilled"));

	private static List<YtmThumbnail> ReadThumbnails(JsonElement parent)
	{
		if (parent.ValueKind != JsonValueKind.Object ||
			!parent.TryGetProperty("thumbnails", out var thumbnails) ||
			thumbnails.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var result = new List<YtmThumbnail>(thumbnails.GetArrayLength());
		foreach (var thumbnail in thumbnails.EnumerateArray())
		{
			if (thumbnail.ValueKind != JsonValueKind.Object || ReadString(thumbnail, "url") is not { } url)
			{
				continue;
			}

			result.Add(new YtmThumbnail(url, ReadInt(thumbnail, "width") ?? 0, ReadInt(thumbnail, "height") ?? 0));
		}

		return result;
	}

	private static string? ReadString(JsonElement element, string property)
		=> element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty(property, out var value) &&
			value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;

	private static int? ReadInt(JsonElement element, string property)
		=> element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty(property, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetInt32(out var number)
				? number
				: null;

	private static double? ReadDouble(JsonElement element, string property)
		=> element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty(property, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetDouble(out var number)
				? number
				: null;

	private static bool? ReadBool(JsonElement element, string property)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
		{
			return null;
		}

		return value.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			_ => null
		};
	}

	private static TEnum ReadEnum<TEnum>(JsonElement element, string property, TEnum fallback)
		where TEnum : struct, Enum
		=> ReadInt(element, property) is { } value && Enum.IsDefined(typeof(TEnum), value)
			? (TEnum)(object)value
			: fallback;

	private static TEnum? ReadNullableEnum<TEnum>(JsonElement element, string property, TEnum unknown)
		where TEnum : struct, Enum
	{
		var value = ReadInt(element, property);
		if (value is not { } number)
		{
			return null;
		}

		return Enum.IsDefined(typeof(TEnum), number) ? (TEnum)(object)number : unknown;
	}
}
