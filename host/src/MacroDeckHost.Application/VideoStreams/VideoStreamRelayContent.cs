namespace MacroDeckHost.Application.VideoStreams;

public enum VideoStreamRelayContentKind
{
	Rejected,
	Media,
	Playlist,
	SniffForPlaylistElseMedia,
	SniffForPlaylistElseRejected
}

public static class VideoStreamRelayContent
{
	public const string PlaylistMediaType = "application/vnd.apple.mpegurl";

	public static bool IsPlaylistPath(string absolutePath)
		=> absolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);

	public static VideoStreamRelayContentKind Classify(string transport, string? mediaType, bool playlistPath)
		=> transport switch
		{
			"mjpeg" => IsOneOf(mediaType, "multipart/x-mixed-replace", "image/jpeg")
				? VideoStreamRelayContentKind.Media
				: VideoStreamRelayContentKind.Rejected,
			"hls" => ClassifyHls(mediaType, playlistPath),
			_ => VideoStreamRelayContentKind.Rejected
		};

	private static VideoStreamRelayContentKind ClassifyHls(string? mediaType, bool playlistPath)
	{
		if (IsOneOf(mediaType, "application/vnd.apple.mpegurl", "application/x-mpegurl"))
		{
			return VideoStreamRelayContentKind.Playlist;
		}

		var isMedia = IsOneOf(mediaType, "video/mp2t", "video/mp4", "video/iso.segment", "text/vtt") ||
			(mediaType is not null && mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase));
		var isUntypedBytes = mediaType is null || IsOneOf(mediaType, "application/octet-stream");
		var isPlainText = IsOneOf(mediaType, "text/plain");

		if (playlistPath)
		{
			return isMedia || isUntypedBytes || isPlainText
				? VideoStreamRelayContentKind.Playlist
				: VideoStreamRelayContentKind.Rejected;
		}

		if (isMedia)
		{
			return VideoStreamRelayContentKind.Media;
		}

		if (isUntypedBytes)
		{
			return VideoStreamRelayContentKind.SniffForPlaylistElseMedia;
		}

		return isPlainText
			? VideoStreamRelayContentKind.SniffForPlaylistElseRejected
			: VideoStreamRelayContentKind.Rejected;
	}

	private static bool IsOneOf(string? mediaType, params string[] allowed)
		=> mediaType is not null && allowed.Any(type => string.Equals(type, mediaType, StringComparison.OrdinalIgnoreCase));
}
