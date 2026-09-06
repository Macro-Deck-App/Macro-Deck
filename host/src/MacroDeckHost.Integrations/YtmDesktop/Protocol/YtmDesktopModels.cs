namespace MacroDeckHost.Integrations.YtmDesktop.Protocol;

internal enum YtmTrackState
{
	Unknown = -1,
	Paused = 0,
	Playing = 1,
	Buffering = 2
}

internal enum YtmRepeatMode
{
	Unknown = -1,
	None = 0,
	All = 1,
	One = 2
}

internal enum YtmLikeStatus
{
	Unknown = -1,
	Dislike = 0,
	Indifferent = 1,
	Like = 2
}

internal enum YtmVideoType
{
	Unknown = -1,
	Audio = 0,
	Video = 1,
	Uploaded = 2,
	Podcast = 3
}

internal sealed record YtmThumbnail(string Url, int Width, int Height);

internal sealed record YtmQueueItem(
	string VideoId,
	string Title,
	string? Author,
	string? Duration,
	bool Selected,
	IReadOnlyList<YtmThumbnail> Thumbnails);

internal sealed record YtmVideoInfo(
	string Id,
	string Title,
	string? Author,
	string? ChannelId,
	string? Album,
	string? AlbumId,
	YtmLikeStatus? LikeStatus,
	IReadOnlyList<YtmThumbnail> Thumbnails,
	int DurationSeconds,
	bool? IsLive,
	YtmVideoType? VideoType,
	bool? MetadataFilled);

internal sealed record YtmQueueInfo(YtmRepeatMode RepeatMode, int SelectedItemIndex, IReadOnlyList<YtmQueueItem> Items);

internal sealed record YtmPlayerInfo(
	YtmTrackState TrackState,
	double VideoProgress,
	int Volume,
	bool? Muted,
	bool AdPlaying,
	YtmQueueInfo? Queue);

internal sealed record YtmPlayerState(YtmPlayerInfo Player, YtmVideoInfo? Video);

internal sealed record YtmPlaylist(string Id, string Title);

internal static class YtmCommands
{
	public const string PlayPause = "playPause";
	public const string Play = "play";
	public const string Pause = "pause";
	public const string VolumeUp = "volumeUp";
	public const string VolumeDown = "volumeDown";
	public const string SetVolume = "setVolume";
	public const string Mute = "mute";
	public const string Unmute = "unmute";
	public const string SeekTo = "seekTo";
	public const string Next = "next";
	public const string Previous = "previous";
	public const string RepeatMode = "repeatMode";

	public const string Shuffle = "shuffle";

	public const string PlayQueueIndex = "playQueueIndex";
	public const string ToggleLike = "toggleLike";
	public const string ToggleDislike = "toggleDislike";
	public const string ChangeVideo = "changeVideo";
}

internal static class YtmErrorCodes
{
	public const string InvalidVolume = "INVALID_VOLUME";
	public const string InvalidRepeatMode = "INVALID_REPEAT_MODE";
	public const string InvalidSeekPosition = "INVALID_SEEK_POSITION";
	public const string InvalidQueueIndex = "INVALID_QUEUE_INDEX";
	public const string InvalidChangeRequest = "INVALID_CHANGE_REQUEST";
	public const string Unauthenticated = "UNAUTHENTICATED";
	public const string AuthorizationDisabled = "AUTHORIZATION_DISABLED";
	public const string AuthorizationInvalid = "AUTHORIZATION_INVALID";
	public const string AuthorizationTimeOut = "AUTHORIZATION_TIME_OUT";
	public const string AuthorizationDenied = "AUTHORIZATION_DENIED";
	public const string AuthorizationTooMany = "AUTHORIZATION_TOO_MANY";

	public const string YoutubeMusicUnavailable = "YOUTUBE_MUSIC_UNVAILABLE";

	public const string YoutubeMusicTimeOut = "YOUTUBE_MUSIC_TIME_OUT";
}
